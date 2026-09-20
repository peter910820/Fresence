module Tests

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
              AlbumTitle = "Album name" } }

    let activity = DiscordActivityMapper.fromMediaSession session

    Assert.Equal("Song title", activity.Details)
    Assert.Equal("Artist name — Album name", activity.State)
