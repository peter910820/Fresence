module Fresence.Media

open System.Threading.Tasks
open Windows.Media.Control

type PlaybackState =
    | Playing
    | Paused
    | Stopped
    | Unavailable

type Track =
    { Title: string
      Artist: string
      AlbumTitle: string }

type MediaSession =
    { SourceAppUserModelId: string
      PlaybackState: PlaybackState
      Track: Track }

type IMediaSessionReader =
    abstract GetSessionsAsync: unit -> Task<MediaSession list>

module MediaSessionMapper =
    let playbackState status =
        match status with
        | GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing -> Playing
        | GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused -> Paused
        | GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped -> Stopped
        | _ -> Unavailable

type Browser =
    | Edge
    | Chrome
    | Firefox

type SelectedMediaSession =
    { Browser: Browser
      Session: MediaSession }

module BrowserMediaSessionSelector =
    let private browserFor (sourceAppUserModelId: string) =
        let source = sourceAppUserModelId.ToLowerInvariant()

        if source.Contains("msedge") then
            Some Edge
        elif source.Contains("chrome") then
            Some Chrome
        elif source.Contains("firefox") then
            Some Firefox
        else
            None

    let private priority browser =
        match browser with
        | Edge -> 0
        | Chrome -> 1
        | Firefox -> 2

    let private playbackPriority playbackState =
        match playbackState with
        | Playing -> 0
        | Paused -> 1
        | Stopped -> 2
        | Unavailable -> 3

    /// <summary>
    /// 從媒體工作階段中選出應同步的瀏覽器來源。
    /// 只保留 Edge、Chrome、Firefox；先依播放狀態（Playing → Paused → Stopped → Unavailable），
    /// 再依瀏覽器優先順序（Edge → Chrome → Firefox）排序，並回傳第一個；沒有符合來源時回傳 None。
    /// </summary>
    let select (sessions: MediaSession list) =
        sessions
        |> List.choose (fun session ->
            match browserFor session.SourceAppUserModelId with
            | Some browser ->
                Some
                    { Browser = browser
                      Session = session }
            | _ -> None)
        |> List.sortBy (fun selected ->
            playbackPriority selected.Session.PlaybackState,
            priority selected.Browser)
        |> List.tryHead

module MediaSessionChangeDetector =
    let hasChanged previous current = previous <> current

type WindowsMediaSessionReader() =
    let readSession (session: GlobalSystemMediaTransportControlsSession) =
        task {
            let! properties =
                session.TryGetMediaPropertiesAsync()
                |> System.WindowsRuntimeSystemExtensions.AsTask

            return
                { SourceAppUserModelId = session.SourceAppUserModelId
                  PlaybackState =
                    session.GetPlaybackInfo().PlaybackStatus
                    |> MediaSessionMapper.playbackState
                  Track =
                    { Title = properties.Title
                      Artist = properties.Artist
                      AlbumTitle = properties.AlbumTitle } }
        }

    interface IMediaSessionReader with
        member _.GetSessionsAsync() =
            task {
                let! manager =
                    GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                    |> System.WindowsRuntimeSystemExtensions.AsTask

                let! sessions =
                    manager.GetSessions()
                    |> Seq.map readSession
                    |> Task.WhenAll

                return List.ofArray sessions
            }
