Imports System.IO
Imports System.Linq
Imports System.Text
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

''' <summary>Builds a single mixtape MP3 by concatenating songs in order: each one is decoded,
''' resampled/up-mixed to a shared 44.1kHz 16-bit stereo format, and trimmed to what's left of
''' the tape, then the resulting WAV segments are spliced together at the byte level and the
''' whole thing is encoded to MP3. Sticks entirely to NAudio's own built-in provider/writer
''' classes (WdlResamplingSampleProvider, MonoToStereoSampleProvider, OffsetSampleProvider,
''' WaveFileWriter/WaveFileReader, MediaFoundationEncoder) rather than implementing
''' ISampleProvider/IWaveProvider directly: in this NAudio version those interfaces read into a
''' Span(Of T), which Visual Basic cannot declare in a member signature.
'''
''' Also hand-writes a minimal ID3v2.3 tag (Title + a custom TXXX:LabelFont frame) onto the
''' finished MP3 so a mixtape's label — and the handwritten-style font it was labeled in — travel
''' with the file itself rather than living only in its filename. ID3v2 tags are designed to be
''' prepended, so this never touches the encoded audio bytes.</summary>
Public Class MixtapeBuilder

    Private Const TargetSampleRate As Integer = 44100
    Private Const TargetChannels As Integer = 2
    Private Const Mp3BitRate As Integer = 192000

    ' OffsetSampleProvider.Take rounds a TimeSpan down to a sample count internally, so a
    ' requested-vs-actually-written segment length can differ by a handful of ticks. That leaves
    ' "remaining" a hair above zero instead of landing exactly on it - and Take treats that
    ' near-zero remainder as "no limit set" rather than "stop almost immediately", so the next
    ' segment gets written in full instead of trimmed to a sliver. Stopping once under a second
    ' remains sidesteps that edge case entirely; losing under a second off a 60/90-minute tape is
    ' not worth a segment for anyway.
    Private Shared ReadOnly MinWorthwhileRemainder As TimeSpan = TimeSpan.FromSeconds(1)

    Public Shared ReadOnly SixtyMinutes As TimeSpan = TimeSpan.FromMinutes(60)
    Public Shared ReadOnly NinetyMinutes As TimeSpan = TimeSpan.FromMinutes(90)

    ''' <summary>Reads just a file's declared duration (fast — doesn't decode the audio).</summary>
    Public Shared Function GetDuration(path As String) As TimeSpan
        Using reader As New AudioFileReader(path)
            Return reader.TotalTime
        End Using
    End Function

    ''' <summary>Full path to the folder where named mixtapes are kept (created on first use).</summary>
    Public Shared Function GetMixtapesDirectory() As String
        Dim dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mixtapes")
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    ''' <summary>Resolves where a mixtape should be written. A blank <paramref name="mixtapeName"/>
    ''' reuses <paramref name="defaultFileName"/> in the app's base directory — the classic 60/90-
    ''' minute "blank tape" that gets re-recorded over each time. A real name is sanitized into a
    ''' filename and saved as a new, distinct file under the Mixtapes folder, numbered (2), (3)...
    ''' if that name's already taken, so a named mixtape is never silently overwritten.</summary>
    Public Shared Function ResolveOutputPath(mixtapeName As String, defaultFileName As String) As String
        If String.IsNullOrWhiteSpace(mixtapeName) Then
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, defaultFileName)
        End If

        Dim sanitized = SanitizeFileName(mixtapeName)
        Dim dir = GetMixtapesDirectory()
        Dim candidate = Path.Combine(dir, sanitized & ".mp3")
        Dim suffix = 1
        While File.Exists(candidate)
            suffix += 1
            candidate = Path.Combine(dir, $"{sanitized} ({suffix}).mp3")
        End While
        Return candidate
    End Function

    ''' <summary>Strips characters that aren't valid in a Windows filename, so arbitrary user
    ''' input becomes a safe file name; falls back to "Mixtape" if nothing usable is left.</summary>
    Private Shared Function SanitizeFileName(name As String) As String
        Dim invalid = Path.GetInvalidFileNameChars()
        Dim cleaned = New String(name.Where(Function(c) Not invalid.Contains(c)).ToArray()).Trim()
        Return If(String.IsNullOrWhiteSpace(cleaned), "Mixtape", cleaned)
    End Function

    ''' <summary>Concatenates <paramref name="songPaths"/> in order into a single MP3 at
    ''' <paramref name="outputPath"/>, trimmed to <paramref name="capacity"/>. Works entirely
    ''' through temp files and swaps the finished MP3 into place at the end, so a failed build
    ''' never leaves a half-written tape behind. If <paramref name="label"/> is given, it's
    ''' embedded as the file's ID3 title (and <paramref name="fontFamily"/>, if given, as the
    ''' font that label should be displayed in) once the swap is done.</summary>
    Public Shared Sub Build(songPaths As IReadOnlyList(Of String), outputPath As String, capacity As TimeSpan,
                             Optional label As String = Nothing, Optional fontFamily As String = Nothing)
        Dim tempDir = Path.Combine(Path.GetTempPath(), "TapePlayerMixtape_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Try
            Dim combinedWavPath = Path.Combine(tempDir, "combined.wav")
            Dim targetFormat As New WaveFormat(TargetSampleRate, 16, TargetChannels)
            Dim remaining = capacity

            Using combinedWriter As New WaveFileWriter(combinedWavPath, targetFormat)
                For i = 0 To songPaths.Count - 1
                    If remaining <= MinWorthwhileRemainder Then Exit For

                    Dim segmentPath = Path.Combine(tempDir, $"segment{i}.wav")
                    WriteSongSegment(songPaths(i), segmentPath, remaining)

                    Using segmentReader As New WaveFileReader(segmentPath)
                        remaining -= segmentReader.TotalTime
                        segmentReader.CopyTo(combinedWriter)
                    End Using
                Next
            End Using

            Dim tempMp3Path = Path.Combine(tempDir, "combined.mp3")
            Using combinedReader As New WaveFileReader(combinedWavPath)
                MediaFoundationEncoder.EncodeToMp3(combinedReader, tempMp3Path, Mp3BitRate)
            End Using

            If File.Exists(outputPath) Then File.Delete(outputPath)
            File.Move(tempMp3Path, outputPath)

            If Not String.IsNullOrWhiteSpace(label) Then WriteLabel(outputPath, label, fontFamily)
        Finally
            Try
                Directory.Delete(tempDir, recursive:=True)
            Catch
                ' Best-effort cleanup only; a stray temp folder isn't worth failing the build over.
            End Try
        End Try
    End Sub

    ''' <summary>Decodes one song — resampled/up-mixed to the shared target format and cut off at
    ''' <paramref name="maxLength"/> — into a standalone 16-bit PCM WAV at <paramref name="segmentPath"/>.</summary>
    Private Shared Sub WriteSongSegment(songPath As String, segmentPath As String, maxLength As TimeSpan)
        Using reader As New AudioFileReader(songPath)
            Dim sp As ISampleProvider = reader

            If sp.WaveFormat.SampleRate <> TargetSampleRate Then
                sp = New WdlResamplingSampleProvider(sp, TargetSampleRate)
            End If
            If sp.WaveFormat.Channels = 1 Then
                sp = New MonoToStereoSampleProvider(sp)
            End If

            Dim limited As New OffsetSampleProvider(sp) With {.Take = maxLength}
            WaveFileWriter.CreateWaveFile16(segmentPath, limited)
        End Using
    End Sub

    ' ---- ID3v2.3 tag read/write (Title + a custom TXXX:LabelFont frame) --------

    ''' <summary>Writes (replacing any existing one) an ID3v2.3 tag holding <paramref name="label"/>
    ''' as the Title (TIT2) frame and, if given, <paramref name="fontFamily"/> as a custom
    ''' TXXX:LabelFont frame.</summary>
    Public Shared Sub WriteLabel(path As String, label As String, Optional fontFamily As String = Nothing)
        Dim audioBytes = StripExistingId3Tag(File.ReadAllBytes(path))

        Dim frames As New List(Of Byte())
        frames.Add(BuildTitleFrame(label))
        If Not String.IsNullOrWhiteSpace(fontFamily) Then
            frames.Add(BuildTxxxFrame("LabelFont", fontFamily))
        End If

        Dim frameBytes = Concat(frames.ToArray())
        Dim header = Concat(
            Encoding.ASCII.GetBytes("ID3"),
            {CByte(3), CByte(0), CByte(0)},
            WriteSyncSafeInt(frameBytes.Length))

        File.WriteAllBytes(path, Concat(header, frameBytes, audioBytes))
    End Sub

    ''' <summary>Reads back a mixtape's embedded label (ID3v2 Title) and, if set, the font it
    ''' should be displayed in. Either comes back Nothing/blank if the file has no ID3v2 tag, no
    ''' Title frame, or no LabelFont frame.</summary>
    Public Shared Function ReadLabel(path As String) As (Label As String, FontFamily As String)
        Try
            Using stream = File.OpenRead(path)
                Dim header(9) As Byte
                If stream.Read(header, 0, 10) <> 10 Then Return (Nothing, Nothing)
                If Encoding.ASCII.GetString(header, 0, 3) <> "ID3" Then Return (Nothing, Nothing)

                Dim majorVersion = header(3)
                Dim tagSize = ReadSyncSafeInt(header, 6)
                Dim tagBytes(Math.Max(tagSize - 1, -1)) As Byte
                If tagSize = 0 OrElse stream.Read(tagBytes, 0, tagSize) <> tagSize Then Return (Nothing, Nothing)

                Dim label As String = Nothing
                Dim fontFamily As String = Nothing
                Dim pos = 0

                While pos + 10 <= tagBytes.Length
                    Dim frameId = Encoding.ASCII.GetString(tagBytes, pos, 4)
                    If frameId = Chr(0) & Chr(0) & Chr(0) & Chr(0) Then Exit While

                    Dim frameSize = If(majorVersion >= 4,
                        ReadSyncSafeInt(tagBytes, pos + 4),
                        (tagBytes(pos + 4) << 24) Or (tagBytes(pos + 5) << 16) Or (tagBytes(pos + 6) << 8) Or tagBytes(pos + 7))
                    If frameSize <= 0 Then Exit While

                    Dim contentStart = pos + 10
                    If contentStart + frameSize > tagBytes.Length Then Exit While

                    If frameId = "TIT2" Then
                        label = DecodeId3Text(tagBytes, contentStart, frameSize)
                    ElseIf frameId = "TXXX" Then
                        Dim entry = DecodeTxxx(tagBytes, contentStart, frameSize)
                        If entry.Description = "LabelFont" Then fontFamily = entry.Value
                    End If

                    pos = contentStart + frameSize
                End While

                Return (label, fontFamily)
            End Using
        Catch
            Return (Nothing, Nothing)
        End Try
    End Function

    ''' <summary>If <paramref name="fileBytes"/> starts with an existing ID3v2 tag, returns just
    ''' the audio that follows it, so re-labeling a mixtape replaces its tag rather than stacking
    ''' another one in front.</summary>
    Private Shared Function StripExistingId3Tag(fileBytes As Byte()) As Byte()
        If fileBytes.Length >= 10 AndAlso Encoding.ASCII.GetString(fileBytes, 0, 3) = "ID3" Then
            Dim tagSize = ReadSyncSafeInt(fileBytes, 6)
            Dim tagEnd = 10 + tagSize
            If tagEnd <= fileBytes.Length Then Return fileBytes.Skip(tagEnd).ToArray()
        End If
        Return fileBytes
    End Function

    Private Shared Function BuildTitleFrame(text As String) As Byte()
        Dim bom As Byte() = {&HFF, &HFE}
        Dim content = Concat({CByte(1)}, bom, Encoding.Unicode.GetBytes(text))
        Return BuildFrame("TIT2", content)
    End Function

    ''' <summary>A user-defined text frame: description and value are plain ASCII/Latin-1 here
    ''' (font family names always are), which keeps the frame simple — no BOM or wide-char null
    ''' terminator to juggle the way TIT2's UTF-16 text needs.</summary>
    Private Shared Function BuildTxxxFrame(description As String, value As String) As Byte()
        Dim content = Concat(
            {CByte(0)},
            Encoding.Latin1.GetBytes(description),
            {CByte(0)},
            Encoding.Latin1.GetBytes(value))
        Return BuildFrame("TXXX", content)
    End Function

    Private Shared Function BuildFrame(frameId As String, content As Byte()) As Byte()
        Return Concat(
            Encoding.ASCII.GetBytes(frameId),
            BigEndianBytes(content.Length),
            {CByte(0), CByte(0)},
            content)
    End Function

    ''' <summary>Decodes a text frame's content (encoding byte + text) per its declared encoding:
    ''' Latin-1, UTF-16 with a BOM (little- or big-endian), or UTF-8.</summary>
    Private Shared Function DecodeId3Text(bytes As Byte(), offset As Integer, length As Integer) As String
        If length < 1 Then Return String.Empty
        Dim encodingByte = bytes(offset)
        Dim textStart = offset + 1
        Dim textLength = length - 1
        If textLength <= 0 Then Return String.Empty

        Dim text As String
        Select Case encodingByte
            Case 1 ' UTF-16 with BOM
                If textLength >= 2 AndAlso bytes(textStart) = &HFF AndAlso bytes(textStart + 1) = &HFE Then
                    text = Encoding.Unicode.GetString(bytes, textStart + 2, textLength - 2)
                ElseIf textLength >= 2 AndAlso bytes(textStart) = &HFE AndAlso bytes(textStart + 1) = &HFF Then
                    text = Encoding.BigEndianUnicode.GetString(bytes, textStart + 2, textLength - 2)
                Else
                    text = Encoding.Unicode.GetString(bytes, textStart, textLength)
                End If
            Case 3 ' UTF-8 (ID3v2.4)
                text = Encoding.UTF8.GetString(bytes, textStart, textLength)
            Case Else ' 0 = ISO-8859-1 (Latin-1)
                text = Encoding.Latin1.GetString(bytes, textStart, textLength)
        End Select
        Return text.TrimEnd(Chr(0))
    End Function

    ''' <summary>Splits a Latin-1-encoded TXXX frame's content into its description and value
    ''' (the two halves this class ever writes via <see cref="BuildTxxxFrame"/>).</summary>
    Private Shared Function DecodeTxxx(bytes As Byte(), offset As Integer, length As Integer) As (Description As String, Value As String)
        If length < 1 OrElse bytes(offset) <> 0 Then Return (Nothing, Nothing)

        Dim contentStart = offset + 1
        Dim contentEnd = offset + length
        Dim nullIndex = Array.IndexOf(bytes, CByte(0), contentStart, contentEnd - contentStart)
        If nullIndex < 0 Then Return (Nothing, Nothing)

        Dim description = Encoding.Latin1.GetString(bytes, contentStart, nullIndex - contentStart)
        Dim valueStart = nullIndex + 1
        Dim value = Encoding.Latin1.GetString(bytes, valueStart, contentEnd - valueStart)
        Return (description, value)
    End Function

    Private Shared Function ReadSyncSafeInt(bytes As Byte(), offset As Integer) As Integer
        Return (bytes(offset) << 21) Or (bytes(offset + 1) << 14) Or (bytes(offset + 2) << 7) Or bytes(offset + 3)
    End Function

    Private Shared Function WriteSyncSafeInt(value As Integer) As Byte()
        Return {
            CByte((value >> 21) And &H7F),
            CByte((value >> 14) And &H7F),
            CByte((value >> 7) And &H7F),
            CByte(value And &H7F)
        }
    End Function

    Private Shared Function BigEndianBytes(value As Integer) As Byte()
        Return {
            CByte((value >> 24) And &HFF),
            CByte((value >> 16) And &HFF),
            CByte((value >> 8) And &HFF),
            CByte(value And &HFF)
        }
    End Function

    Private Shared Function Concat(ParamArray parts As Byte()()) As Byte()
        Return parts.SelectMany(Function(p) p).ToArray()
    End Function

End Class
