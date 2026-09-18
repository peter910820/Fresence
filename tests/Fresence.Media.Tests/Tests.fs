module Tests

open Fresence.Media
open Windows.Media.Control
open Xunit

let session sourceAppUserModelId playbackState title =
    { SourceAppUserModelId = sourceAppUserModelId
      PlaybackState = playbackState
      Track =
        { Title = title
          Artist = "Artist"
          AlbumTitle = "Album" } }

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

[<Fact>]
let ``選擇播放中的 Edge 工作階段`` () =
    let result =
        [ session "msedge.exe" Playing "Edge song"
          session "chrome.exe" Playing "Chrome song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected -> Assert.Equal(Edge, selected.Browser)
    | None -> failwith "Expected an Edge media session."

[<Fact>]
let ``選擇暫停中的瀏覽器工作階段`` () =
    let result =
        [ session "chrome.exe" Paused "Paused song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected ->
        Assert.Equal(Chrome, selected.Browser)
        Assert.Equal(Paused, selected.Session.PlaybackState)
    | None -> failwith "Expected a paused Chrome media session."

[<Fact>]
let ``選擇已停止的瀏覽器工作階段`` () =
    let result =
        [ session "firefox.exe" Stopped "Stopped song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected ->
        Assert.Equal(Firefox, selected.Browser)
        Assert.Equal(Stopped, selected.Session.PlaybackState)
    | None -> failwith "Expected a stopped Firefox media session."

[<Fact>]
let ``選擇狀態未知的瀏覽器工作階段`` () =
    let result =
        [ session "firefox.exe" Unavailable "Unknown song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected ->
        Assert.Equal(Firefox, selected.Browser)
        Assert.Equal(Unavailable, selected.Session.PlaybackState)
    | None -> failwith "Expected an unavailable Firefox media session."

[<Fact>]
let ``播放中的工作階段優先於暫停中的工作階段`` () =
    let result =
        [ session "msedge.exe" Paused "Paused Edge song"
          session "chrome.exe" Playing "Chrome song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected ->
        Assert.Equal(Chrome, selected.Browser)
        Assert.Equal(Playing, selected.Session.PlaybackState)
    | None -> failwith "Expected a playing Chrome media session."

[<Fact>]
let ``選擇播放中的 Firefox 工作階段`` () =
    let result =
        [ session "firefox.exe" Playing "Firefox song" ]
        |> BrowserMediaSessionSelector.select

    match result with
    | Some selected -> Assert.Equal(Firefox, selected.Browser)
    | None -> failwith "Expected a Firefox media session."

[<Fact>]
let ``歌曲資料變更時偵測為已變更`` () =
    let previous = Some(session "msedge.exe" Playing "First song")
    let current = Some(session "msedge.exe" Playing "Next song")

    let result = MediaSessionChangeDetector.hasChanged previous current

    Assert.True(result)

[<Fact>]
let ``相同歌曲資料不視為變更`` () =
    let current = Some(session "msedge.exe" Playing "Same song")

    let result = MediaSessionChangeDetector.hasChanged current current

    Assert.False(result)
