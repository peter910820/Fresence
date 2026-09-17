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
