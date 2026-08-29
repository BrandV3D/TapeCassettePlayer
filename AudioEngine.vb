Imports NAudio.Wave
Imports NAudio.Effects

''' <summary>Playback engine used in place of WPF's MediaElement. MediaElement can't expose the
''' decoded audio samples for real-time processing, so there was no way to make the Equalizer
''' window actually filter the sound - this wraps NAudio's WaveOut/AudioFileReader instead and
''' threads a persistent <see cref="NAudio.Effects.GraphicEqualizer"/> into the sample pipeline
''' between them. The WaveOut itself is created once and reused across songs (Stop, swap the
''' reader, Init again) so Volume - and the Equalizer's band gains - survive a track change instead
''' of resetting; Preamp is stored here for the same reason and reapplied to each new reader.</summary>
Public Class AudioEngine
    Implements IDisposable

    Public ReadOnly Equalizer As New GraphicEqualizer(GraphicEqualizerLayout.TenBandOctave)

    Private ReadOnly waveOut As New WaveOut()
    Private reader As AudioFileReader
    Private stoppingIntentionally As Boolean
    Private preampGain As Single = 1

    ''' <summary>Raised when playback stops because the track reached its end on its own - not when
    ''' <see cref="Stop"/> or <see cref="Load"/> caused it, so callers can tell "song finished" apart
    ''' from "I stopped it myself" the way MediaElement's MediaEnded used to.</summary>
    Public Event PlaybackEnded()

    Public Sub New()
        AddHandler waveOut.PlaybackStopped, AddressOf WaveOut_PlaybackStopped
    End Sub

    Public ReadOnly Property IsLoaded As Boolean
        Get
            Return reader IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property Duration As TimeSpan
        Get
            Return If(reader?.TotalTime, TimeSpan.Zero)
        End Get
    End Property

    Public Property Position As TimeSpan
        Get
            Return If(reader?.CurrentTime, TimeSpan.Zero)
        End Get
        Set(value As TimeSpan)
            If reader IsNot Nothing Then reader.CurrentTime = value
        End Set
    End Property

    ''' <summary>Master output level (0-1), applied after the equalizer.</summary>
    Public Property Volume As Single
        Get
            Return waveOut.Volume
        End Get
        Set(value As Single)
            waveOut.Volume = value
        End Set
    End Property

    ''' <summary>Pre-equalizer gain trim (the Preamp knob), as a linear multiplier - 1 = unity.
    ''' Kept here (not just on the reader) so it survives the reader being replaced on every
    ''' <see cref="Load"/>.</summary>
    Public Property Preamp As Single
        Get
            Return preampGain
        End Get
        Set(value As Single)
            preampGain = value
            If reader IsNot Nothing Then reader.Volume = value
        End Set
    End Property

    Public Sub Load(filePath As String)
        stoppingIntentionally = True
        waveOut.Stop()
        reader?.Dispose()

        reader = New AudioFileReader(filePath) With {.Volume = preampGain}
        Dim eqProvider As New EffectSampleProvider(reader, Equalizer)
        waveOut.Init(eqProvider)
    End Sub

    Public Sub Play()
        waveOut.Play()
    End Sub

    Public Sub Pause()
        waveOut.Pause()
    End Sub

    Public Sub [Stop]()
        stoppingIntentionally = True
        waveOut.Stop()
    End Sub

    Private Sub WaveOut_PlaybackStopped(sender As Object, e As StoppedEventArgs)
        Dim wasIntentional = stoppingIntentionally
        stoppingIntentionally = False
        If Not wasIntentional Then RaiseEvent PlaybackEnded()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        waveOut.Dispose()
        reader?.Dispose()
    End Sub

End Class
