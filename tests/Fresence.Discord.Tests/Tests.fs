module Tests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Fresence.Discord
open Fresence.Media
open Xunit

type StubHttpMessageHandler(responseBody: string) =
    inherit HttpMessageHandler()

    let mutable requestCount = 0

    member _.RequestCount = requestCount

    override _.SendAsync(_, _) =
        requestCount <- requestCount + 1
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(responseBody)
        Task.FromResult response

// 建立 iTunes 封面查找測試用的歌曲資料。
let track title artist =
    { Title = title
      Artist = artist
      AlbumTitle = "" }

// 建立會回傳指定 JSON 的 iTunes 封面查找服務。
let artworkProvider responseBody =
    let handler = new StubHttpMessageHandler(responseBody)
    let client = new HttpClient(handler)
    new ItunesAlbumArtworkProvider(client, TimeSpan.FromSeconds 1.0), client, handler

// 驗證歌曲資料會映射為 Discord 的 Details 與 State。
[<Fact>]
let ``歌曲資料會映射為 Discord Activity`` () =
    let session =
        { SourceAppUserModelId = "msedge.exe"
          PlaybackState = Playing
          Track =
            { Title = "Song title"
              Artist = "Artist name"
              AlbumTitle = "Album name" }
          Timeline =
            { Position = TimeSpan.Zero
              Duration = None } }

    let activity = DiscordActivityMapper.fromMediaSession session

    Assert.Equal("Song title", activity.Details)
    Assert.Equal("Artist name", activity.State)
    Assert.Equal(None, activity.Timestamps)

// 驗證播放中的歌曲會映射為 Discord 進度 timestamps。
[<Fact>]
let ``播放中的歌曲會映射為 Discord timestamps`` () =
    let session =
        { SourceAppUserModelId = "msedge.exe"
          PlaybackState = Playing
          Track =
            { Title = "Song title"
              Artist = "Artist name"
              AlbumTitle = "Album name" }
          Timeline =
            { Position = TimeSpan.FromSeconds 30.0
              Duration = Some(TimeSpan.FromMinutes 3.0) } }

    let activity = DiscordActivityMapper.fromMediaSession session

    match activity.Timestamps with
    | Some(startTime, endTime) ->
        Assert.Equal(TimeSpan.FromMinutes 3.0, endTime - startTime)
    | None -> failwith "Expected Discord timestamps."

// 驗證超出 Discord 長度限制的文字會被截斷。
[<Fact>]
let ``過長的歌曲資料會截斷至 Discord 限制`` () =
    let text = String.replicate 129 "a"

    let session =
        { SourceAppUserModelId = "msedge.exe"
          PlaybackState = Playing
          Track =
            { Title = text
              Artist = text
              AlbumTitle = text }
          Timeline =
            { Position = TimeSpan.Zero
              Duration = None } }

    let activity = DiscordActivityMapper.fromMediaSession session

    Assert.Equal(128, activity.Details.Length)
    Assert.Equal(128, activity.State.Length)

// 驗證 iTunes 的正確歌曲結果會回傳高解析度封面 URL。
[<Fact>]
let ``iTunes 正確歌曲結果會回傳封面 URL`` () =
    let response =
        """
        {
          "results": [
            {
              "trackName": "Bibbidiba",
              "artistName": "Hoshimachi Suisei",
              "artworkUrl100": "https://example.com/cover/100x100bb.jpg"
            }
          ]
        }
        """

    let provider, client, _ = artworkProvider response
    use _ = client

    let artworkUrl =
        (provider :> IAlbumArtworkProvider)
            .GetArtworkUrlAsync(track "Bibbidiba" "Hoshimachi Suisei")
            .Result

    Assert.Equal(Some "https://example.com/cover/600x600bb.jpg", artworkUrl)

// 驗證不同語言或拼音的演出者名稱仍採用 iTunes 的第一筆封面。
[<Fact>]
let ``iTunes 演出者名稱不相同時仍會回傳封面`` () =
    let response =
        """
        {
          "results": [
            {
              "trackName": "Same song",
              "artistName": "Another artist",
              "artworkUrl100": "https://example.com/cover/100x100bb.jpg"
            }
          ]
        }
        """

    let provider, client, _ = artworkProvider response
    use _ = client

    let artworkUrl =
        (provider :> IAlbumArtworkProvider)
            .GetArtworkUrlAsync(track "Same song" "Expected artist")
            .Result

    Assert.Equal(Some "https://example.com/cover/600x600bb.jpg", artworkUrl)

// 驗證 iTunes 無封面資料時不會回傳無效圖片 URL。
[<Fact>]
let ``iTunes 無封面資料時不會回傳 URL`` () =
    let response =
        """
        {
          "results": [
            {
              "trackName": "Song title",
              "artistName": "Artist name"
            }
          ]
        }
        """

    let provider, client, _ = artworkProvider response
    use _ = client

    let artworkUrl =
        (provider :> IAlbumArtworkProvider)
            .GetArtworkUrlAsync(track "Song title" "Artist name")
            .Result

    Assert.Equal(None, artworkUrl)

// 驗證相同歌曲在快取期間不會重複查詢 iTunes。
[<Fact>]
let ``iTunes 封面會在快取期間重複使用`` () =
    let response =
        """
        {
          "results": [
            {
              "artworkUrl100": "https://example.com/cover/100x100bb.jpg"
            }
          ]
        }
        """

    let provider, client, handler = artworkProvider response
    use _ = client

    let artworkProvider = provider :> IAlbumArtworkProvider
    let currentTrack = track "Song title" "Artist name"

    artworkProvider.GetArtworkUrlAsync(currentTrack).Wait()
    artworkProvider.GetArtworkUrlAsync(currentTrack).Wait()

    Assert.Equal(1, handler.RequestCount)
