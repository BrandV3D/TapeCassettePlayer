Imports System.Windows.Threading

''' <summary>Companion window showing a scrolling oscilloscope-style waveform trace. Cassette owns
''' it alongside the other companion windows and calls <see cref="SetPlaying"/> whenever playback
''' starts or stops. Like EqualizerWindow, there's no real-time access to the decoded audio samples
''' (MediaElement doesn't expose them), so the trace is a few layered, slowly-drifting sine waves
''' plus noise - it settles to a flat line the moment playback stops, same as the bar equalizer.</summary>
Class VisualizerWindow

    Private Const SampleCount As Integer = 160
    Private ReadOnly renderTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(1000 / 45)}
    Private ReadOnly rng As New Random()

    ' Rolling sample buffer: index 0 is the oldest (leftmost) point, the newest sample is appended
    ' at the end each tick and everything shifts left - the classic scrolling-scope motion.
    Private samples(SampleCount - 1) As Double

    Private elapsedSeconds As Double
    Private energy As Double
    Private targetEnergy As Double

    ' Three sine components at different speeds, each with its own slow LFO drifting its frequency
    ' over time, summed together so the trace looks like a real, ever-changing signal instead of an
    ' obvious repeating loop.
    Private ReadOnly baseFreq As Double() = {0.8, 2.3, 5.1}
    Private ReadOnly baseWeight As Double() = {0.55, 0.3, 0.15}
    Private ReadOnly phase As Double() = {0, 0, 0}
    Private ReadOnly driftFreq As Double() = {0.05, 0.08, 0.11}
    Private ReadOnly driftPhase As Double()

    Public Sub New()
        InitializeComponent()

        driftPhase = {rng.NextDouble() * Math.PI * 2, rng.NextDouble() * Math.PI * 2, rng.NextDouble() * Math.PI * 2}
        phase(0) = rng.NextDouble() * Math.PI * 2
        phase(1) = rng.NextDouble() * Math.PI * 2
        phase(2) = rng.NextDouble() * Math.PI * 2

        AddHandler renderTimer.Tick, AddressOf RenderTimer_Tick
        AddHandler Me.SizeChanged, Sub() BuildGridLines()
        AddHandler Me.Loaded, Sub()
                                   BuildGridLines()
                                   renderTimer.Start()
                               End Sub
        AddHandler Me.Closed, Sub() renderTimer.Stop()
    End Sub

    ''' <summary>Tells the visualizer whether audio is actually playing. The trace eases its energy
    ''' toward full amplitude while playing and back down to a flat centre line when stopped.</summary>
    Public Sub SetPlaying(isPlaying As Boolean)
        targetEnergy = If(isPlaying, 1.0, 0.0)
    End Sub

    Private Sub BuildGridLines()
        GridLinesCanvas.Children.Clear()
        Dim w = ScopeRoot.ActualWidth
        Dim h = ScopeRoot.ActualHeight
        If w <= 0 OrElse h <= 0 Then Return

        Dim lineBrush As New SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
        lineBrush.Freeze()

        Const hLines As Integer = 4
        For i = 1 To hLines
            Dim y = h * i / (hLines + 1)
            Dim line As New Rectangle With {.Width = w, .Height = 1, .Fill = lineBrush}
            Canvas.SetTop(line, y)
            GridLinesCanvas.Children.Add(line)
        Next

        Const vLines As Integer = 7
        For i = 1 To vLines
            Dim x = w * i / (vLines + 1)
            Dim line As New Rectangle With {.Width = 1, .Height = h, .Fill = lineBrush}
            Canvas.SetLeft(line, x)
            GridLinesCanvas.Children.Add(line)
        Next

        Dim centerBrush As New SolidColorBrush(Color.FromArgb(35, 255, 255, 255))
        centerBrush.Freeze()
        Dim centerLine As New Rectangle With {.Width = w, .Height = 1, .Fill = centerBrush}
        Canvas.SetTop(centerLine, h / 2)
        GridLinesCanvas.Children.Add(centerLine)
    End Sub

    Private Sub RenderTimer_Tick(sender As Object, e As EventArgs)
        Dim dt = renderTimer.Interval.TotalSeconds
        elapsedSeconds += dt
        energy += (targetEnergy - energy) * Math.Min(1, dt * 3)
        If Math.Abs(energy - targetEnergy) < 0.001 Then energy = targetEnergy

        Dim w = ScopeRoot.ActualWidth
        Dim h = ScopeRoot.ActualHeight
        If w <= 0 OrElse h <= 0 Then Return

        ' Compose the newest incoming sample and scroll the buffer.
        Dim signal As Double = 0
        For i = 0 To 2
            Dim f = baseFreq(i) * (1 + 0.4 * Math.Sin(elapsedSeconds * driftFreq(i) + driftPhase(i)))
            signal += baseWeight(i) * Math.Sin(elapsedSeconds * f * Math.PI * 2 + phase(i))
        Next
        signal += (rng.NextDouble() - 0.5) * 0.12
        signal = Math.Max(-1, Math.Min(1, signal)) * energy

        Array.Copy(samples, 1, samples, 0, SampleCount - 1)
        samples(SampleCount - 1) = signal

        Dim centerY = h / 2
        Dim amplitude = h / 2 * 0.85
        Dim stepX = w / (SampleCount - 1)

        Dim points As New PointCollection(SampleCount)
        Dim fillPoints As New PointCollection(SampleCount + 2)
        fillPoints.Add(New Point(0, h))
        For i = 0 To SampleCount - 1
            Dim x = i * stepX
            Dim y = centerY - samples(i) * amplitude
            points.Add(New Point(x, y))
            fillPoints.Add(New Point(x, y))
        Next
        fillPoints.Add(New Point(w, h))

        WaveLine.Points = points
        WaveFill.Points = fillPoints

        Canvas.SetLeft(LeadDot, w - LeadDot.Width / 2)
        Canvas.SetTop(LeadDot, centerY - samples(SampleCount - 1) * amplitude - LeadDot.Height / 2)
        LeadDot.Opacity = If(energy > 0.02, 0.9, 0)
    End Sub

End Class
