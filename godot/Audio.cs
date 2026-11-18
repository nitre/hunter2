using System.Linq;
using System.Text.Json;
using Godot;
using Hunter.Rules;

/// <summary>The game's sound as plain samples (user 2026-10-15: no Paula emulation): four players
/// for the four channels, fed by Channels.Changed, and one music player. Files and loop points
/// come from game_data/audio/audio.json (tools/audio). An effect plays until a new sound takes its channel or
/// its own countdown runs out (Channels.Tick); volume 0 mutes it; music plays outside the game only.</summary>
public partial class Audio : Node
{
    readonly AudioStreamPlayer[] _ch = new AudioStreamPlayer[4];
    readonly AudioStreamPlayer _music = new();
    AudioStreamWav?[] _fx = [];
    AudioStream?[] _songs = [];
    /// <summary>Each effect's own period (the manifest's, = the driver's +$430 row): what the WAV
    /// plays at PitchScale 1. The game's channel table is one row off (+$422), so an engine's live
    /// period is not this - PitchScale divides it out.</summary>
    float[] _ownPeriod = [];
    /// <summary>The sound each channel is playing (volume moves, sound -1, keep its pitch).</summary>
    readonly int[] _playing = [-1, -1, -1, -1];
    int _song = -1;
    /// <summary>The last song asked for by number: music_in_play resumes the turn from here.</summary>
    int _lastSong;
    /// <summary>music_in_play: the song is playing quietly under the hunt (EnterPlay); a plain
    /// Music call is always the original's full level.</summary>
    bool _quiet;
    /// <summary>sound_position: each channel's current pan (-1 left .. 1 right), and the bus panner
    /// when the setting is on (otherwise null and _pan is only kept for the tails).</summary>
    readonly float[] _pan = new float[4];
    readonly AudioEffectPanner?[] _panFx = new AudioEffectPanner?[4];
    /// <summary>sound_tails: spare players a cut-off effect fades out on, round-robin; enough for
    /// all four channels losing their sound at once (ResetChannels).</summary>
    readonly AudioStreamPlayer?[] _tail = new AudioStreamPlayer?[4];
    readonly float[] _tailGain = new float[4];
    readonly long[] _tailAt = new long[4];
    int _tailNext;
    /// <summary>SOUND VOICES: MORE (Channels.Spare): spare players for what the four channels leave out, centred on the
    /// master bus at the channels' level; logged as channels 4.. (make_video plays them centred). Key: the thing's slot
    /// for an engine (kept while it goes without a channel, silent 3 frames after the last pass that asked), -1 a
    /// one-shot, -2 free.</summary>
    const int Spares = 8;
    readonly AudioStreamPlayer[] _spare = new AudioStreamPlayer[Spares];
    readonly int[] _spareKey = Enumerable.Repeat(-2, Spares).ToArray();
    readonly int[] _spareSeen = new int[Spares];

    public static Audio? Instance { get; private set; }
    /// <summary>Verification only: HUNTER_AUDIO_LOG=1 prints every sound started.</summary>
    static readonly bool Log = System.Environment.GetEnvironmentVariable("HUNTER_AUDIO_LOG") == "1";
    /// <summary>Verification only: HUNTER_SOUND_LOG=path writes every start and stop of a player with the
    /// headless frame it happened in (Frame, set by Main's capture loop), so tools/make_video.py can mix
    /// the soundtrack the run would have played. Lines: frame play ch sound volume pitch | frame stop ch |
    /// frame music song - and, only when the enhancements are on: frame pan ch pan | frame tail ch |
    /// frame musicdb db | 0 smooth | 0 slowmo from to factor.</summary>
    static readonly System.IO.StreamWriter? SoundLog = System.Environment.GetEnvironmentVariable("HUNTER_SOUND_LOG") is { } sl
        ? new System.IO.StreamWriter(sl) { AutoFlush = true } : null;
    public static int Frame;

    public void Init(string gameData)
    {
        Instance = this;
        var dir = System.IO.Path.Combine(gameData, "audio");
        var manifest = System.IO.Path.Combine(dir, "audio.json");
        if (!System.IO.File.Exists(manifest)) { GD.Print("HUNTER: no audio (game_data/audio/audio.json missing)"); return; }
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
        _fx = doc.RootElement.GetProperty("sfx").EnumerateArray().Select(e =>
            Load(dir, e, e.GetProperty("looping").GetBoolean())).ToArray();
        _ownPeriod = doc.RootElement.GetProperty("sfx").EnumerateArray()
            .Select(e => (float)e.GetProperty("period").GetInt32()).ToArray();
        _songs = doc.RootElement.GetProperty("music").EnumerateArray().Select(e => LoadSong(dir, e)).ToArray();
        if (System.Environment.GetEnvironmentVariable("HUNTER_SLOWMO") is { } slow) SoundLog?.WriteLine("0 slowmo " + slow.Replace(':', ' '));   // from to factor: the tape slows there (make_video.py)
        for (var i = 0; i < 4; i++)
            AddChild(_ch[i] = new AudioStreamPlayer { Bus = Hunter.Settings.SoundPosition ? ChannelBus(i) : SideBus(i is 0 or 3 ? -1 : 1) });
        if (Hunter.Settings.SoundTails)
            for (var i = 0; i < 4; i++) AddChild(_tail[i] = new AudioStreamPlayer());
        AddChild(_music);
        for (var i = 0; i < Spares; i++) AddChild(_spare[i] = new AudioStreamPlayer());
        Channels.Changed += OnChannel;
        Channels.Spare += OnSpare;
        ApplyFilter(Hunter.Settings.A500Filter);
        GD.Print($"HUNTER: audio {_fx.Length} effects, {_songs.Length} songs");
    }

    /// <summary>The Amiga 500's fixed output filter on the master bus (Settings.A500Filter): one pole at
    /// 4.42 kHz, 6 dB per octave - the A500's RC stage. Effects and music both go through it, as on the
    /// machine.</summary>
    public static void ApplyFilter(bool on)
    {
        var bus = AudioServer.GetBusIndex("Master");
        for (var i = AudioServer.GetBusEffectCount(bus) - 1; i >= 0; i--)
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter) AudioServer.RemoveBusEffect(bus, i);
        if (on) AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 4420, Db = AudioEffectFilter.FilterDB.Filter6Db });
    }

    /// <summary>Paula is hard stereo: channels 0 and 3 left, 1 and 2 right (music_render mixes the songs the
    /// same). A bus with a full pan sends a mono effect to one side only.</summary>
    static string SideBus(int pan)
    {
        var name = pan < 0 ? "PaulaLeft" : "PaulaRight";
        if (AudioServer.GetBusIndex(name) >= 0) return name;
        var bus = AudioServer.BusCount;
        AudioServer.AddBus();
        AudioServer.SetBusName(bus, name);
        AudioServer.SetBusSend(bus, "Master");
        AudioServer.AddBusEffect(bus, new AudioEffectPanner { Pan = pan });
        return name;
    }

    /// <summary>sound_position: one bus per channel with its own panner (not AudioStreamPlayer2D: the
    /// pan is a plain screen-x function of the owner, there is no distance law or listener to move -
    /// the game's own volume curve already is the distance law - and 2D players would rewire the
    /// whole start/volume path for nothing). The panner interpolates across each mix block, so a
    /// per-frame pan move does not click. Chosen centres: the player's own sounds (owner 0) and any
    /// owner behind the camera (z + $100 <= 0, where the Persp projection is invalid) pan centre.</summary>
    string ChannelBus(int i)
    {
        var name = $"PaulaCh{i}";
        if (AudioServer.GetBusIndex(name) >= 0) return name;
        var bus = AudioServer.BusCount;
        AudioServer.AddBus();
        AudioServer.SetBusName(bus, name);
        AudioServer.SetBusSend(bus, "Master");
        _panFx[i] = new AudioEffectPanner { Pan = 0 };
        AudioServer.AddBusEffect(bus, _panFx[i]);
        return name;
    }

    /// <summary>A tail's bus: its own panner, frozen at the channel's last pan (sound_position on) or
    /// the channel's hard side as ever (off). Set at handoff, so a tail keeps its pan while the
    /// channel's own bus moves on with the new sound.</summary>
    string TailBus(int ch)
    {
        var name = $"PaulaTail{ch}";
        var bus = AudioServer.GetBusIndex(name);
        if (bus < 0)
        {
            bus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(bus, name);
            AudioServer.SetBusSend(bus, "Master");
            AudioServer.AddBusEffect(bus, new AudioEffectPanner());
        }
        ((AudioEffectPanner)AudioServer.GetBusEffect(bus, 0)).Pan = Hunter.Settings.SoundPosition ? _pan[ch] : (ch is 0 or 3 ? -1 : 1);
        return name;
    }

    /// <summary>One channel's level against the music's: music_render puts a channel at x64 per sample byte
    /// on its side, an effect WAV holds x128 (render_sfx GAIN 0.5), and the panner at full pan adds the mono
    /// stream's left and right into the one side (Godot AudioEffectPannerInstance::process: left*1 +
    /// right*(1-0)) - so x1/4 brings an effect to the same level per channel as the Amiga plays both.</summary>
    static readonly float ChannelDb = Mathf.LinearToDb(0.25f);

    /// <summary>sound_position's gain. Godot's panner is linear, not constant-power: a mono stream
    /// comes out (1-pan) left and (1+pan) right, so centre puts the untouched stream on BOTH sides -
    /// total power (0.25 v)² against the hard-panned (2 x 0.25 v)²/2 = 0.125 v² now: 3 dB down. The
    /// x sqrt(2) here (-9.03 dB instead of -12 dB) makes a CENTRED effect exactly as loud in total as
    /// one hard-panned is today; an owner at the screen edge is then +3 dB on its side (Godot's law
    /// sweeps 0..+3 dB across the pan whatever the gain - there is no setting that is flat).</summary>
    static readonly float PanDb = ChannelDb + 3.0103f;

    /// <summary>The high wind (docs/sounds.md 18, the ambient sound when high): muted for now.</summary>
    const int HighWind = 18;

    public override void _ExitTree()
    {
        Channels.Changed -= OnChannel;
        Channels.Spare -= OnSpare;
        if (Instance == this) Instance = null;
    }

    static AudioStreamWav? Load(string dir, JsonElement e, bool loop)
    {
        var path = System.IO.Path.Combine(dir, e.GetProperty("file").GetString()!);
        if (!System.IO.File.Exists(path)) return null;
        var wav = AudioStreamWav.LoadFromFile(path);
        if (loop) Loop(wav, e);
        return wav;
    }

    /// <summary>The manifest's loop points, when it gives them.</summary>
    static void Loop(AudioStreamWav wav, JsonElement e)
    {
        if (!e.TryGetProperty("loop_end", out var end) || end.ValueKind != JsonValueKind.Number) return;
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = e.GetProperty("loop_start").GetInt32();
        wav.LoopEnd = end.GetInt32();
    }

    /// <summary>A song: OGG Vorbis (looped whole from loop_start) or a WAV (looped at its loop points).</summary>
    AudioStream? LoadSong(string dir, JsonElement e)
    {
        var path = System.IO.Path.Combine(dir, e.GetProperty("file").GetString()!);
        if (path.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase))
        {
            // the songs as OGG Vorbis (a sixth of the WAV): each file cut at its loop end, so it loops whole from loop_start
            var ogg = AudioStreamOggVorbis.LoadFromFile(path);
            ogg.Loop = true;
            ogg.LoopOffset = e.GetProperty("loop_start").GetInt32() / 44100.0;
            return ogg;
        }
        var wav = AudioStreamWav.LoadFromFile(path);
        Loop(wav, e);
        return wav;
    }

    /// <summary>SetChannel / UpdateEntitySound: start sound on channel ch at volume 0-63, or
    /// (sound -1) only move its volume; period is the value the original hands the driver this
    /// pass (base on a start, base - 4 x speed on an update). Volume 0 is the game's way of
    /// silencing a channel. Pitch: the driver starts an effect at its own table's period
    /// (+$430, PitchScale 1), then the game's per-pass PER takes over - the base comes from the
    /// game's one-row-off +$422 row, exactly as the original sounds.</summary>
    void OnChannel(int ch, int sound, int volume, int period)
    {
        ch &= 3;
        var p = _ch[ch];
        if (p is null) return;
        if (Log && sound >= 0) GD.Print($"HUNTER: sound {sound} on channel {ch} volume {volume}");
        // the sample player ($113A6): a start ($11412) plays the effect from its first sample; after that
        // only its volume and period move ($11590-$115BC, every tick while its countdown runs) - volume 0
        // is silence, not an end; the countdown running out ($114E4, Channels.Tick -> sound -2) ends it
        if (sound == -2) { p.Stop(); _playing[ch] = -1; SoundLog?.WriteLine($"{Frame} stop {ch}"); return; }
        var gain = volume <= 0 ? -80f : Mathf.LinearToDb(volume / 64f) + (Hunter.Settings.SoundPosition ? PanDb : ChannelDb);
        if (sound < 0)
        {
            sound = _playing[ch];
            if (sound < 0) return;                  // no effect running: the move reaches nothing
            p.VolumeDb = gain;
            // PER $2DFA: the effect's own sample rate times own period / new period = 3546895/period
            if (period > 0 && sound < _ownPeriod.Length) p.PitchScale = _ownPeriod[sound] / period;
            SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} vol {ch} {volume} {p.PitchScale}"));
            return;
        }
        if (sound >= _fx.Length || _fx[sound] is not { } wav) { p.Stop(); _playing[ch] = -1; SoundLog?.WriteLine($"{Frame} stop {ch}"); return; }
        if (sound == HighWind) gain = -80f;           // muted for now (owner 2026-10-31): a 1428-byte noise loop at 1.7 kHz, an odd pulsing whoosh; the channel still runs as the game's

        // sound_tails: the channel's sound was still sounding (the countdown has not run out - a
        // -2 already reset _playing - and it is not muted at volume 0): let it fade on a spare
        // player instead of the new start cutting it dead. 80 ms: long enough to mask the click of
        // the cut, short enough not to smear a fast re-trigger (an engine restarts within a pass).
        if (Hunter.Settings.SoundTails && _playing[ch] >= 0 && p.Playing && p.VolumeDb > -79f)
        {
            var i = _tailNext;
            _tailNext = (_tailNext + 1) % _tail.Length;
            if (_tail[i] is { } tail)
            {
                tail.Stream = p.Stream;
                tail.PitchScale = p.PitchScale;
                tail.VolumeDb = p.VolumeDb;
                tail.Bus = TailBus(ch);
                tail.Play(p.GetPlaybackPosition());      // carry on from where the cut sound was, not from its start
                _tailGain[i] = p.VolumeDb;
                _tailAt[i] = (long)Time.GetTicksMsec();
                SoundLog?.WriteLine($"{Frame} tail {ch}");
            }
        }
        _playing[ch] = sound;
        p.PitchScale = 1;                           // a start: the driver's own +$430 row (see above)
        p.VolumeDb = gain;
        p.Stream = wav;
        p.Play();
        SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} play {ch} {sound} {volume} {p.PitchScale}"));
    }

    /// <summary>Channels.Spare: a sound the four channels left out, on a spare voice (see _spare).</summary>
    void OnSpare(int key, int sound, int volume, int period)
    {
        if (key == -3) { for (var i = 0; i < Spares; i++) StopSpare(i); return; }   // LeavePlaySound: all quiet
        if (sound < 0 || sound >= _fx.Length || _fx[sound] is not { } wav || sound == HighWind) return;
        var gain = volume <= 0 ? -80f : Mathf.LinearToDb(volume / 64f) + ChannelDb;
        var pitch = period > 0 && sound < _ownPeriod.Length ? _ownPeriod[sound] / period : 1f;
        if (key >= 0 && System.Array.IndexOf(_spareKey, key) is var held and >= 0)
        {                                                                   // an engine still without a channel: move it on
            _spare[held].VolumeDb = gain;
            _spare[held].PitchScale = pitch;
            _spareSeen[held] = Frame;
            SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} vol {4 + held} {volume} {pitch}"));
            return;
        }
        var at = -1;                                                        // a free voice, else the quietest one
        for (var i = 0; i < Spares; i++)
            if (_spareKey[i] == -2 || !_spare[i].Playing) { at = i; break; }
        if (at < 0)
            for (var i = 0; i < Spares; i++)
                if (at < 0 || _spare[i].VolumeDb < _spare[at].VolumeDb) at = i;
        if (_spare[at].VolumeDb > gain && _spare[at].Playing && _spareKey[at] != -2) return;   // all louder than this one
        StopSpare(at);
        var p = _spare[at];
        p.Stream = wav;
        p.VolumeDb = gain;
        p.PitchScale = key >= 0 ? pitch : 1f;                               // a one-shot starts at its own period, as a channel start
        p.Play();
        _spareKey[at] = key;
        _spareSeen[at] = Frame;
        SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} play {4 + at} {sound} {volume} {p.PitchScale}"));
    }

    void StopSpare(int i)
    {
        if (_spareKey[i] == -2) return;
        _spare[i].Stop();
        _spareKey[i] = -2;
        SoundLog?.WriteLine($"{Frame} stop {4 + i}");
    }

    /// <summary>Whether music is on now (StopMusicAndCheck $B00A stops it; that routine's $161A
    /// add is copy protection, left out of the port).</summary>
    public bool MusicPlaying => _song >= 0 && _music.Playing;

    /// <summary>Song 0-2 looping, or -1 for none (PlayMusic $AFBE's songs; StopMusic in play). A
    /// plain call is always the original's full level: it clears music_in_play's quiet flag, so
    /// play ending (PlayEndSong, the StopMusic callers) is the original's music handling again.</summary>
    public void Music(int song)
    {
        if (Log) GD.Print($"HUNTER: music {song}");
        if (song == _song && !_quiet) return;
        if (song != _song)
        {
            _song = song;
            _music.Stop();
            SoundLog?.WriteLine($"{Frame} music {song}");
        }
        _quiet = false;
        _music.VolumeDb = 0;
        if (song < 0 || song >= _songs.Length || _songs[song] is not { } wav) return;
        _lastSong = song;
        _music.Stream = wav;
        _music.Play();
    }

    /// <summary>music_in_play, at Main.StartGame's Music(-1) hook (the original's "music never
    /// plays in play"): off = exactly Music(-1); on = the song that was on keeps going quietly for
    /// the hunt (12 dB down - under the effects, still there), or, when nothing was playing, the
    /// songs 0-2 in turn from the last one asked for.</summary>
    public void EnterPlay()
    {
        if (!Hunter.Settings.MusicInPlay) { Music(-1); return; }
        if (_song < 0 || !_music.Playing)
        {
            if (_songs.Length == 0) { Music(-1); return; }
            Music((_lastSong + 1) % _songs.Length);
        }
        _quiet = true;
        _music.VolumeDb = QuietDb;
        SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} musicdb {QuietDb}"));
    }

    /// <summary>music_in_play's level under a hunt: 12 dB below the menu level.</summary>
    static readonly float QuietDb = -12f;

    /// <summary>sound_position, once a game frame from GameView.Frame (after play.Draw, so a start
    /// this pass also gets its pan this pass): pan each channel by where its owner is on screen -
    /// the Persp projection of the owner's camera-relative x (+$56) and z (+$5A) over the 320-wide
    /// screen, clamped to the edges. Owner 0 (the player) and an owner behind the camera (where
    /// z + $100 <= 0 makes the projection invalid) stay centre.</summary>
    public void SetPan(int ch, float pan)
    {
        _pan[ch] = pan;
        if (!Hunter.Settings.SoundPosition) return;
        SoundLog?.WriteLine(System.FormattableString.Invariant($"{Frame} pan {ch} {pan}"));
        if (_panFx[ch] is { } fx) fx.Pan = pan;      // the panner smooths it across the mix block
    }

    public override void _Process(double delta)
    {
        // sound_tails' fades: 80 ms from the gain at handoff down to -60 dB (then silence), real
        // time so it sounds the same however fast the game runs.
        for (var i = 0; i < Spares; i++)                                    // SOUND VOICES: an engine that has its channel back
            if (_spareKey[i] >= 0 && Frame - _spareSeen[i] > 3) StopSpare(i);   // (or stopped) leaves its spare voice
            else if (_spareKey[i] == -1 && _spare[i] is { Playing: false }) _spareKey[i] = -2;   // a one-shot that has ended
        var now = (long)Time.GetTicksMsec();
        for (var i = 0; i < _tail.Length; i++)
        {
            if (_tailAt[i] == 0 || _tail[i] is not { } t) continue;
            var k = (now - _tailAt[i]) / 80.0;
            if (k >= 1) { t.Stop(); _tailAt[i] = 0; }
            else t.VolumeDb = Mathf.Lerp(_tailGain[i], -60f, (float)k);
        }
    }
}
