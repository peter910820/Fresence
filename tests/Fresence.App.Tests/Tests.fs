module Tests

open System
open System.Threading.Tasks
open Fresence.App
open Fresence.Discord
open Fresence.Media
open Xunit

// 建立測試用的播放中媒體工作階段。
let session sourceAppUserModelId title =
    { SourceAppUserModelId = sourceAppUserModelId
      PlaybackState = Playing
      Track =
        { Title = title
          Artist = "Artist"
          AlbumTitle = "Album" }
      Timeline =
        { Position = TimeSpan.Zero
          Duration = None } }

type FakeMediaSessionReader(sessions: MediaSession list) =
    interface IMediaSessionReader with
        // 回傳建立測試替身時指定的媒體工作階段。
        member _.GetSessionsAsync() = Task.FromResult sessions

type FakeDiscordPresenceClient() =
    let mutable connectionCount = 0
    let mutable activities: DiscordActivity list = []
    let mutable clearCount = 0

    member _.ConnectionCount = connectionCount
    member _.Activities = activities
    member _.ClearCount = clearCount

    interface IDiscordPresenceClient with
        // 模擬成功建立 Discord 連線。
        member _.ConnectAsync() =
            connectionCount <- connectionCount + 1
            Task.FromResult(Ok ())

        // 記錄收到的 Activity，供測試驗證。
        member _.SetActivityAsync(activity) =
            activities <- activity :: activities
            Task.FromResult(Ok ())

        // 記錄清除 Presence 的呼叫次數。
        member _.ClearActivityAsync() =
            clearCount <- clearCount + 1
            Task.FromResult(Ok ())

type FakeAlbumArtworkProvider(artworkUrl: string option) =
    interface IAlbumArtworkProvider with
        // 回傳建立測試替身時指定的封面 URL。
        member _.GetArtworkUrlAsync(_) = Task.FromResult artworkUrl

// 驗證第一次同步會將歌曲映射並更新至 Discord。
[<Fact>]
let ``新歌曲會更新 Discord Presence`` () =
    let discordClient = FakeDiscordPresenceClient ()
    let artworkProvider = FakeAlbumArtworkProvider None

    let synchronizer =
        PresenceSynchronizer(
            FakeMediaSessionReader [ session "msedge.exe" "Song title" ],
            discordClient,
            artworkProvider
        )

    let result = (synchronizer.SynchronizeAsync ()).Result

    Assert.Equal(Updated, result)
    Assert.Equal(1, discordClient.ConnectionCount)
    Assert.Equal(1, discordClient.Activities.Length)
    Assert.Equal("Song title", discordClient.Activities.Head.Details)

// 驗證相同選取結果不會造成重複的 Discord 更新。
[<Fact>]
let ``未變更的歌曲不會重複更新 Discord Presence`` () =
    let discordClient = FakeDiscordPresenceClient ()
    let artworkProvider = FakeAlbumArtworkProvider None

    let synchronizer =
        PresenceSynchronizer(
            FakeMediaSessionReader [ session "msedge.exe" "Same song" ],
            discordClient,
            artworkProvider
        )

    (synchronizer.SynchronizeAsync ()).Wait()
    let result = (synchronizer.SynchronizeAsync ()).Result

    Assert.Equal(Skipped, result)
    Assert.Equal(1, discordClient.Activities.Length)

// 驗證沒有可選取歌曲時會清除 Discord Presence。
[<Fact>]
let ``沒有可同步歌曲時清除 Discord Presence`` () =
    let discordClient = FakeDiscordPresenceClient ()
    let artworkProvider = FakeAlbumArtworkProvider None
    let synchronizer = PresenceSynchronizer(FakeMediaSessionReader [], discordClient, artworkProvider)

    let result = (synchronizer.SynchronizeAsync ()).Result

    Assert.Equal(Cleared, result)
    Assert.Equal(1, discordClient.ClearCount)

// 驗證同步歌曲時會將查找到的封面加入 Discord Activity。
[<Fact>]
let ``新歌曲會將專輯封面同步至 Discord`` () =
    let discordClient = FakeDiscordPresenceClient ()
    let artworkProvider = FakeAlbumArtworkProvider(Some "https://example.com/cover.jpg")

    let synchronizer =
        PresenceSynchronizer(
            FakeMediaSessionReader [ session "msedge.exe" "Song title" ],
            discordClient,
            artworkProvider
        )

    (synchronizer.SynchronizeAsync ()).Wait()

    Assert.Equal(Some "https://example.com/cover.jpg", discordClient.Activities.Head.LargeImageUrl)
