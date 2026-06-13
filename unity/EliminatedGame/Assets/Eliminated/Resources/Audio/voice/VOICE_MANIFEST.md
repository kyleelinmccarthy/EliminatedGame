# Game Master announcer voice bank

Announcer voicelines rendered offline with **Piper** (neural TTS, a build-time
tool — not a runtime/Unity dependency), for natural near-web quality. The game
plays/queues these WAVs at runtime (see `Scripts/Audio/Announcer.cs`).

- male announcer  : `norman.onnx`
- female announcer: `en_US-ljspeech-medium.onnx`
- **Ship only CC0 / public-domain voices** (e.g. en_US-joe = CC0, en_US-kristin =
  public domain). Record the chosen voices + licenses in `docs/ASSET_SOURCES.md`.
  Re-run: `PIPER_BIN=… PIPER_MALE=… PIPER_FEMALE=… dotnet run --project tools/VoiceGen -- <dir>`.

16-bit PCM mono WAV. **M** = male announcer (game reveals + Simon Says), **F** = female (eliminations).

- `game_01.wav` — [M] “Game one.”
- `game_02.wav` — [M] “Game two.”
- `game_03.wav` — [M] “Game three.”
- `game_04.wav` — [M] “Game four.”
- `game_05.wav` — [M] “Game five.”
- `game_06.wav` — [M] “Game six.”
- `game_07.wav` — [M] “Game seven.”
- `game_08.wav` — [M] “Game eight.”
- `game_09.wav` — [M] “Game nine.”
- `game_10.wav` — [M] “Game ten.”
- `game_11.wav` — [M] “Game eleven.”
- `game_12.wav` — [M] “Game twelve.”
- `game_13.wav` — [M] “Game thirteen.”
- `game_14.wav` — [M] “Game fourteen.”
- `game_15.wav` — [M] “Game fifteen.”
- `game_16.wav` — [M] “Game sixteen.”
- `game_17.wav` — [M] “Game seventeen.”
- `game_18.wav` — [M] “Game eighteen.”
- `game_19.wav` — [M] “Game nineteen.”
- `game_20.wav` — [M] “Game twenty.”
- `final_game.wav` — [M] “The final game.”
- `attention.wav` — [M] “Attention, players.”
- `name_redlight.wav` — [M] “Red light, green light.”
- `name_tag.wav` — [M] “Freeze tag.”
- `name_mingle.wav` — [M] “Mingle.”
- `name_glassbridge.wav` — [M] “Glass stepping stones.”
- `name_tugofwar.wav` — [M] “Tug of war.”
- `name_rps.wav` — [M] “Rock, paper, scissors. Minus one.”
- `name_jumprope.wav` — [M] “Killer jump rope.”
- `name_boomerang.wav` — [M] “Boomerang brawl.”
- `name_dodgeball.wav` — [M] “Dodgeball.”
- `name_musicalchairs.wav` — [M] “Musical chairs.”
- `name_present.wav` — [M] “Secret Santa sabotage.”
- `name_prophunt.wav` — [M] “Prop hunt.”
- `name_chutesladders.wav` — [M] “Chutes and ladders.”
- `name_simonsays.wav` — [M] “Simon says.”
- `name_keepyuppy.wav` — [M] “Keepy uppy.”
- `name_koth.wav` — [M] “King of the lava islands.”
- `room_courtyard.wav` — [M] “The arena, The Courtyard.”
- `room_neon.wav` — [M] “The arena, Neon District.”
- `room_candy.wav` — [M] “The arena, Candy Kingdom.”
- `room_toxic.wav` — [M] “The arena, The Toxic Works.”
- `room_beach.wav` — [M] “The arena, Sunny Shores.”
- `room_haunt.wav` — [M] “The arena, Haunted Manor.”
- `simon_head.wav` — [M] “Simon says, pat your head.”
- `simon_nose.wav` — [M] “Simon says, touch your nose.”
- `simon_blink.wav` — [M] “Simon says, blink.”
- `simon_flip.wav` — [M] “Simon says, flip.”
- `simon_jump.wav` — [M] “Simon says, jump.”
- `simon_freeze.wav` — [M] “Freeze! Touch nothing.”
- `elim_you.wav` — [F] “You have been eliminated.”
- `elim_player.wav` — [F] “Player eliminated.”
- `elim_players.wav` — [F] “Players eliminated.”
- `num_player.wav` — [F] “Player”
- `num_players.wav` — [F] “Players”
- `num_0.wav` — [F] “zero”
- `num_1.wav` — [F] “one”
- `num_2.wav` — [F] “two”
- `num_3.wav` — [F] “three”
- `num_4.wav` — [F] “four”
- `num_5.wav` — [F] “five”
- `num_6.wav` — [F] “six”
- `num_7.wav` — [F] “seven”
- `num_8.wav` — [F] “eight”
- `num_9.wav` — [F] “nine”
- `num_elim.wav` — [F] “has been eliminated.”
- `num_elim_plural.wav` — [F] “have been eliminated.”
