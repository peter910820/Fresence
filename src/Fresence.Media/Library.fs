module Fresence.Media

open System
open System.Threading.Tasks
open Windows.Foundation
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

type PlaybackTimeline =
    { Position: TimeSpan
      Duration: TimeSpan option }

type MediaSession =
    { SourceAppUserModelId: string
      PlaybackState: PlaybackState
      Track: Track
      Timeline: PlaybackTimeline }

type IMediaSessionReader =
    /// <summary>
    /// 非同步取得目前所有 Windows 媒體工作階段。
    /// </summary>
    abstract GetSessionsAsync: unit -> Task<MediaSession list>

type IMediaSessionChangeNotifier =
    /// <summary>
    /// 訂閱媒體工作階段、歌曲屬性與播放狀態的變更。
    /// 回傳值應在不再需要監聽時釋放。
    /// </summary>
    abstract SubscribeToChangesAsync: (unit -> unit) -> Task<IDisposable>

module MediaSessionMapper =
    /// <summary>
    /// 將 Windows GSMTC 播放狀態轉換為 Fresence 的播放狀態。
    /// </summary>
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
    // 依來源 App ID 判斷支援的瀏覽器。
    let private browserFor (sourceAppUserModelId: string) =
        let source = sourceAppUserModelId.ToLowerInvariant ()

        if source.Contains "msedge" then Some Edge
        elif source.Contains "chrome" then Some Chrome
        elif source.Contains "firefox" then Some Firefox
        else None

    // 回傳瀏覽器優先順序，數字越小優先度越高。
    let private priority browser =
        match browser with
        | Edge -> 0
        | Chrome -> 1
        | Firefox -> 2

    // 回傳播放狀態優先順序，優先顯示正在播放的歌曲。
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
            | Some browser -> Some { Browser = browser; Session = session }
            | _ -> None)
        |> List.sortBy (fun selected -> playbackPriority selected.Session.PlaybackState, priority selected.Browser)
        |> List.tryHead

module MediaSessionChangeDetector =
    /// <summary>
    /// 判斷目前選取的媒體工作階段是否和上次同步的結果不同。
    /// </summary>
    let hasChanged previous current = previous <> current

type WindowsMediaSessionReader() =
    // 將 WinRT 工作階段轉換為 Fresence 的媒體資料模型。
    let readSession (session: GlobalSystemMediaTransportControlsSession) =
        task {
            let! properties =
                session.TryGetMediaPropertiesAsync ()
                |> System.WindowsRuntimeSystemExtensions.AsTask

            let timeline = session.GetTimelineProperties ()
            let duration = timeline.EndTime - timeline.StartTime

            return
                { SourceAppUserModelId = session.SourceAppUserModelId
                  PlaybackState =
                    (session.GetPlaybackInfo ()).PlaybackStatus
                    |> MediaSessionMapper.playbackState
                  Track =
                    { Title = properties.Title
                      Artist = properties.Artist
                      AlbumTitle = properties.AlbumTitle }
                  Timeline =
                    { Position = timeline.Position
                      Duration =
                        if duration > TimeSpan.Zero then
                            Some duration
                        else
                            None } }
        }

    interface IMediaSessionReader with
        /// <summary>
        /// 從 Windows GSMTC 讀取並轉換目前所有媒體工作階段。
        /// </summary>
        member _.GetSessionsAsync() =
            task {
                let! manager =
                    GlobalSystemMediaTransportControlsSessionManager.RequestAsync ()
                    |> System.WindowsRuntimeSystemExtensions.AsTask

                let! sessions = manager.GetSessions () |> Seq.map readSession |> Task.WhenAll

                return List.ofArray sessions
            }

    interface IMediaSessionChangeNotifier with
        /// <summary>
        /// 訂閱 GSMTC 工作階段清單、歌曲屬性與播放狀態變更。
        /// </summary>
        member _.SubscribeToChangesAsync(onChanged) =
            task {
                let! manager =
                    GlobalSystemMediaTransportControlsSessionManager.RequestAsync ()
                    |> System.WindowsRuntimeSystemExtensions.AsTask

                let mutable sessionSubscriptions: IDisposable list = []

                // 訂閱單一工作階段的歌曲屬性與播放狀態事件。
                let subscribeSession (session: GlobalSystemMediaTransportControlsSession) =
                    let propertiesChangedHandler =
                        new TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>(fun
                                                                                                                              _
                                                                                                                              _ ->
                            onChanged ())

                    let playbackChangedHandler =
                        new TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>(fun
                                                                                                                           _
                                                                                                                           _ ->
                            onChanged ())

                    session.add_MediaPropertiesChanged (propertiesChangedHandler)
                    session.add_PlaybackInfoChanged (playbackChangedHandler)

                    [ { new IDisposable with
                          member _.Dispose() =
                              session.remove_MediaPropertiesChanged (propertiesChangedHandler) }
                      { new IDisposable with
                          member _.Dispose() =
                              session.remove_PlaybackInfoChanged (playbackChangedHandler) } ]

                // 工作階段清單改變時，解除舊訂閱並註冊目前的工作階段。
                let refreshSessions () =
                    sessionSubscriptions |> List.iter (fun subscription -> subscription.Dispose ())

                    sessionSubscriptions <- manager.GetSessions () |> Seq.collect subscribeSession |> Seq.toList

                    onChanged ()

                let sessionsChangedHandler =
                    new TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs>(fun
                                                                                                                          _
                                                                                                                          _ ->
                        refreshSessions ())

                manager.add_SessionsChanged (sessionsChangedHandler)

                refreshSessions ()

                return
                    { new IDisposable with
                        member _.Dispose() =
                            manager.remove_SessionsChanged (sessionsChangedHandler)

                            sessionSubscriptions |> List.iter (fun subscription -> subscription.Dispose ()) }
            }
