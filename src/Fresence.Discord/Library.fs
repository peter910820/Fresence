module Fresence.Discord

open System
open System.Threading.Tasks
open DiscordRPC
open Fresence.Media

/// <summary>
/// Discord Rich Presence 的顯示資料。
/// </summary>
type DiscordActivity =
    { Details: string
      State: string
      Timestamps: (DateTime * DateTime) option }

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
          State = $"{session.Track.Artist} — {session.Track.AlbumTitle}" |> limitText
          Timestamps = timestamps }

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
