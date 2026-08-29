''' <summary>Companion window holding just the transport controls. All playback/recording logic
''' stays in Cassette; it wires each button here to its own handler methods, so this class stays
''' passive (no code-behind logic of its own).</summary>
Class ControlsWindow

    Public Sub New()
        InitializeComponent()
    End Sub

End Class
