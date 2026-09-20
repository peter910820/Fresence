module Tests

open System
open Fresence.Discord
open Fresence.Media
open Xunit

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
    Assert.Equal("Artist name — Album name", activity.State)
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
