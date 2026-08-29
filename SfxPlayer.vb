Imports System.IO
Imports NAudio.Wave

''' <summary>Plays the cassette deck's mechanical sound effects (Sfx\*.wav — synthesized button
''' clicks/clunks and a loopable rewind/fast-forward motor whir, since no reliably licensed
''' source of real recorded deck audio was available to pull in here). One-shot effects
''' (<see cref="Play"/>) can overlap freely — each gets its own WaveOut, torn down when it
''' finishes — while the seek whir uses a single dedicated WaveOut that's manually looped by
''' replaying it on <see cref="WaveOut.PlaybackStopped"/>, since this NAudio version ships no
''' LoopStream and a hand-written one isn't possible: NAudio 3.0.1's wave-reading interfaces read
''' into a Span(Of T), which Visual Basic cannot declare in a member signature (see
''' MixtapeBuilder's header comment for the same constraint). Every sound here comes from NAudio's
''' own built-in reader/output classes, never a custom one.</summary>
Public Class SfxPlayer

    Private Shared ReadOnly SfxDirectory As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sfx")

    Private WithEvents loopOutput As WaveOut
    Private loopReader As AudioFileReader
    Private loopActive As Boolean

    ''' <summary>Fire-and-forget playback of a short effect (e.g. "clunk.wav"). Silently does
    ''' nothing if the file is missing, so a missing/corrupt Sfx asset never blocks the actual
    ''' transport action it was attached to.</summary>
    Public Sub Play(fileName As String)
        Dim sfxPath = Path.Combine(SfxDirectory, fileName)
        If Not File.Exists(sfxPath) Then Return

        Try
            Dim reader As New AudioFileReader(sfxPath)
            Dim output As New WaveOut()
            AddHandler output.PlaybackStopped, Sub(sender As Object, e As StoppedEventArgs)
                                                    output.Dispose()
                                                    reader.Dispose()
                                                End Sub
            output.Init(reader)
            output.Play()
        Catch
            ' Best-effort only.
        End Try
    End Sub

    ''' <summary>Starts (or restarts) the looping motor-whir effect used while rewind/fast-forward
    ''' is held, replaying it from the top each time it finishes so it loops seamlessly.</summary>
    Public Sub StartLoop(fileName As String)
        StopLoop()

        Dim sfxPath = Path.Combine(SfxDirectory, fileName)
        If Not File.Exists(sfxPath) Then Return

        Try
            loopReader = New AudioFileReader(sfxPath)
            loopOutput = New WaveOut()
            loopActive = True
            loopOutput.Init(loopReader)
            loopOutput.Play()
        Catch
            loopActive = False
        End Try
    End Sub

    Private Sub LoopOutput_PlaybackStopped(sender As Object, e As StoppedEventArgs) Handles loopOutput.PlaybackStopped
        If Not loopActive Then Return
        loopReader.Position = 0
        loopOutput.Play()
    End Sub

    Public Sub StopLoop()
        loopActive = False
        loopOutput?.Stop()
        loopOutput?.Dispose()
        loopOutput = Nothing
        loopReader?.Dispose()
        loopReader = Nothing
    End Sub

End Class
