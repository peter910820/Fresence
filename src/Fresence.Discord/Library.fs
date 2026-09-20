module Fresence.Discord

open System.Net.Http
open System.Text.Json
open System
open System.Threading.Tasks
open DiscordRPC
open Fresence.Media
open Microsoft.Extensions.Caching.Memory

/// <summary>
/// 查找歌曲專輯封面 URL 的契約。
/// </summary>
type IAlbumArtworkProvider =
    abstract GetArtworkUrlAsync: Track -> Task<string option>

/// <summary>
/// 透過 iTunes Search API 查找歌曲專輯封面的實作。
/// </summary>
type ItunesAlbumArtworkProvider(httpClient: HttpClient, timeout: TimeSpan) =
    let cacheLifetime = TimeSpan.FromMinutes 30.0
    let cacheOptions = new MemoryCacheOptions()

    do
        cacheOptions.ExpirationScanFrequency <- TimeSpan.FromMinutes 1.0

    let cache = new MemoryCache(cacheOptions)

    // 將歌曲名稱與演出者正規化，以避免空白與大小寫影響比對。
    let normalize (value: string) = value.Trim().ToUpperInvariant()

    // 建立用於快取與 iTunes 查詢的歌曲識別字串。
    let trackKey (track: Track) =
        $"{normalize track.Artist}|{normalize track.Title}"

    // 建立 iTunes Search API 請求 URI。
    let searchUri (track: Track) =
        let term = Uri.EscapeDataString $"{track.Artist} {track.Title}"
        Uri $"https://itunes.apple.com/search?term={term}&media=music&entity=song&limit=5"

    // 讀取 JSON 物件中的非空白字串欄位。
    let tryGetString (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, property when property.ValueKind = JsonValueKind.String ->
            property.GetString()
            |> Option.ofObj
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
        | _ -> None

    // 將 iTunes 的縮圖 URL 改為可用於 Rich Presence 的較高解析度版本。
    let tryCreateArtworkUrl (value: string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttps ->
            uri.AbsoluteUri.Replace("100x100bb", "600x600bb", StringComparison.Ordinal)
            |> Some
        | _ -> None

    // 依 iTunes 搜尋排序，挑出第一筆具有有效封面 URL 的結果。
    let tryFindArtworkUrl (response: string) =
        use document = JsonDocument.Parse response

        match document.RootElement.TryGetProperty "results" with
        | true, results when results.ValueKind = JsonValueKind.Array ->
            results.EnumerateArray()
            |> Seq.tryPick (tryGetString "artworkUrl100" >> Option.bind tryCreateArtworkUrl)
        | _ -> None

    // 查詢 iTunes；任何網路或資料錯誤都會回傳無封面。
    let searchAsync (track: Track) =
        task {
            if String.IsNullOrWhiteSpace track.Title || String.IsNullOrWhiteSpace track.Artist then
                return None
            else
                try
                    use cancellationSource = new Threading.CancellationTokenSource(timeout)

                    let! response = httpClient.GetAsync(searchUri track, cancellationSource.Token)

                    if not response.IsSuccessStatusCode then
                        return None
                    else
                        let! body = response.Content.ReadAsStringAsync cancellationSource.Token
                        return tryFindArtworkUrl body
                with _ ->
                    return None
        }

    interface IAlbumArtworkProvider with
        /// <summary>
        /// 查找歌曲封面，並快取成功與失敗結果 30 分鐘。
        /// </summary>
        member _.GetArtworkUrlAsync track =
            cache.GetOrCreateAsync(
                trackKey track,
                Func<ICacheEntry, Task<string option>>(fun entry ->
                    entry.AbsoluteExpirationRelativeToNow <- Nullable cacheLifetime
                    searchAsync track)
            )

    interface IDisposable with
        /// <summary>
        /// 釋放專輯封面快取資源。
        /// </summary>
        member _.Dispose() = cache.Dispose()

/// <summary>
/// Discord Rich Presence 的顯示資料。
/// </summary>
type DiscordActivity =
    { Details: string
      State: string
      Timestamps: (DateTime * DateTime) option
      LargeImageUrl: string option
      LargeImageText: string option }

/// <summary>
/// 管理 Discord 本機 IPC 連線與 Rich Presence 更新的契約。
/// </summary>
type IDiscordPresenceClient =
    abstract ConnectAsync: unit -> Task<Result<unit, string>>
    abstract SetActivityAsync: DiscordActivity -> Task<Result<unit, string>>
    abstract ClearActivityAsync: unit -> Task<Result<unit, string>>

/// <summary>
/// 將 Fresence 的媒體資料轉換為 Discord 顯示資料。
/// </summary>
module DiscordActivityMapper =
    let private maximumTextLength = 125

    /// <summary>
    /// 將 Discord 限制長度以外的文字截斷，避免 Rich Presence 被拒絕。
    /// </summary>
    let private limitText value =
        if String.IsNullOrEmpty value || value.Length <= maximumTextLength then
            value
        else
            value.Substring(0, maximumTextLength) + "..."

    /// <summary>
    /// 映射資料。
    /// </summary>
    let fromMediaSession (session: MediaSession) =
        let timestamps =
            match session.PlaybackState, session.Timeline.Duration with
            | Playing, Some duration ->
                let startTime = DateTime.UtcNow - session.Timeline.Position
                Some(startTime, startTime + duration)
            | _ -> None

        { Details = limitText session.Track.Title
          State = limitText session.Track.Artist
          Timestamps = timestamps
          LargeImageUrl = None
          LargeImageText = None }

    /// <summary>
    /// 將專輯封面資料加入 Discord Activity。
    /// </summary>
    let withArtwork artworkUrl (session: MediaSession) =
        let activity = fromMediaSession session

        { activity with
            LargeImageUrl = artworkUrl
            LargeImageText = None }

/// <summary>
/// 透過 DiscordRichPresence 用戶端實作 Rich Presence IPC。
/// </summary>
type DiscordIpcClient(applicationId: string) =
    let client = new DiscordRpcClient(applicationId)

    // 執行 Discord 用戶端操作並將例外轉換為 Result。
    let execute action =
        task {
            try
                action ()
                return Ok()
            with ex ->
                return Error ex.Message
        }

    interface IDiscordPresenceClient with
        /// <summary>
        /// 初始化 Discord Rich Presence 用戶端。
        /// </summary>
        member _.ConnectAsync() =
            execute (fun () -> client.Initialize() |> ignore)

        /// <summary>
        /// 設定目前歌曲的 Discord Rich Presence。
        /// </summary>
        member _.SetActivityAsync activity =
            execute (fun () ->
                let presence =
                    RichPresence(Details = activity.Details, State = activity.State, Type = ActivityType.Listening)

                activity.Timestamps
                |> Option.iter (fun (startTime, endTime) -> presence.Timestamps <- Timestamps(startTime, endTime))

                activity.LargeImageUrl
                |> Option.iter (fun imageUrl ->
                    let assets = Assets(LargeImageKey = imageUrl)

                    activity.LargeImageText
                    |> Option.iter (fun imageText -> assets.LargeImageText <- imageText)

                    presence.Assets <- assets)

                client.SetPresence(presence))

        /// <summary>
        /// 清除 Fresence 設定的 Discord Rich Presence。
        /// </summary>
        member _.ClearActivityAsync() = execute client.ClearPresence

    interface IDisposable with
        /// <summary>
        /// 釋放 Discord Rich Presence 用戶端資源。
        /// </summary>
        member _.Dispose() = client.Dispose()
