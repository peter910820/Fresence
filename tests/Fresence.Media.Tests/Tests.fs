module Tests

open Fresence.Media
open Windows.Media.Control
open Xunit

[<Fact>]
let ``播放狀態 Playing 會映射為 Playing`` () =
    let result =
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
        |> MediaSessionMapper.playbackState

    Assert.Equal(Playing, result)

[<Fact>]
let ``播放狀態 Paused 會映射為 Paused`` () =
    let result =
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused
        |> MediaSessionMapper.playbackState

    Assert.Equal(Paused, result)

[<Fact>]
let ``未處理的播放狀態會映射為 Unavailable`` () =
    let result =
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing
        |> MediaSessionMapper.playbackState

    Assert.Equal(Unavailable, result)
