# THE LAST BLOCK — playable prototype

Online, first-person, physics-based Jenga for 2–5 players (or you against bots) in an old frontier saloon.
No turn limit — the tower stands until somebody drops it, **and whoever drops it loses**. Secret personal tasks,
three experimental cards, host-authoritative physics, English / Türkçe.

---

## Sürüm 0.2'de neler değişti (Türkçe)

* **Kural:** 12 tur hedefi kaldırıldı. Kule yıkılana kadar sırayla oynanır; **kuleyi yıkan kaybeder**, diğer herkes
  kazanır. Beraberlik yok. Sonuç ekranında kaybeden ve kazananlar yazar, damga sana göre *KAZANDIN / KAYBETTİN*.
* **Botlar:** Ana menüde **Botlara karşı tek oyna** (tek tıkla masa kurar ve 2 bot oturtur) ya da lobide kurucu
  **+ bot oturt / - botu kaldır** düğmeleri. Botlar insanlarla aynı fizikle oynar, kuleyi yıkıp kaybedebilirler.
* **Eller ve görüş:** kendi kolunun üst kısmı artık görünmez (kuleyi kapatmaz); blok taşırken el bloğu yakın
  ucundan, alttan tutar; taşıma sırasında kamera hafifçe ayağa kalkıp kulenin tepesine bakar; boş yerler
  parlar; kule uzadıkça oturuş yüksekliği artar. El hareketleri yaylı/yumuşak (SmoothDamp) ve blokla kilitli.
* **Ortam:** eski kovboy salonu — tahta zemin ve lambri, duvar kağıdı, kirişler, aynalı bar ve şişeler, SALOON
  tabelası, WANTED afişleri, piyano, fıçılar, yan masalar, gün batımı pencereleri, yaylı kapılar, masanın üstünde
  mumlu araba tekerleği avize, duvar fenerleri. Işık sıcak ve aydınlık.
* **Türkçe:** Ana menünün sağ üstünde *English / Türkçe* (ya da Ayarlar > Dil). Sunucu mesajları her oyuncuya kendi
  dilinde gösterilir.
* **Karakter:** Quaternius'un **Universal Animation Library** mankeni (CC0), `Tools~/blender_build_cowboy.py` ile
  kovboy şapkası, boyunbağı ve salon kıyafetiyle giydirildi; oturma animasyonlarıyla sandalyelerde oturur.
  Dosya: `Models/Characters/TLB_Cowboy.fbx`.
* Yeni dokular `Tools~/generate_saloon_textures.py` ile üretildi. **Generate Project'i yeniden çalıştırın.**

### Puanlar ve el hareketleri (0.2.1)

* **Maç = birkaç el** (lobide *Maçtaki el sayısı*, varsayılan 3). Her elin puanı toplama eklenir, son elden sonra
  **en yüksek toplam maçı kazanır**. Puanlar (`Game/ScoreRules.cs`):
  * Kuleyi yıkmadın: **+3** · Kuleyi yıktın: **-3**
  * Gizli görev tamam: **Gözü Kara +4**, Sağlam Eller / Titiz Usta / Kule Bekçisi **+3**, Kart Ustası **+2**,
    Baskıcı **+1** (kartı garanti olduğu için) · Görevin en az yarısı (hedef 2+ ise): **+1**
  * Denge: yıkmak, hayatta kalana göre 6 puan kaybettirir; bu hiçbir tek görevden fazla. Yani görev için kuleyi
    riske atmak asla "bedava" değil, ama zor görevi bitiren dikkatli oyuncu (+7) sadece güvenli oynayanı (+3)
    geçer ve 3 ellik maçta bir yıkılış toparlanabilir. Görev değerleri her TaskDefinition'da `bonusPoints` ile değişir.
* **El hareketleri** (sol el, herkes görür, botlar da yapar): **1** tamam işareti · **2** başparmak · **3** orta parmak
  · **4** önündeki bira bardağından bir yudum · **5** el sallama · **6** rock işareti.

## Hızlı başlangıç (Türkçe)

1. Projeyi Unity **6000.3.9f1** ile açın. Script'ler derlendikten sonra menüden
   **Tools > The Last Block > Generate Project** çalıştırın. Bu; yeraltı odasını, prefab'ları, materyalleri,
   görev/kart asset'lerini ve `Assets/SecretJenga/Scenes/TheLastBlock.unity` sahnesini oluşturur
   (eski oturma odası asset'lerini siler). Tekrar çalıştırmak güvenlidir.
2. Sahneyi açın, **Play**. Tek başına: **Botlara karşı tek oyna** > lobide **Başla**.
   Arkadaşlarla: *Masa kur > Doğrudan (IP / LAN) > Masayı aç*.
3. İkinci oyuncu: ikinci bir build açın (Build Settings > Build) ya da *Multiplayer Play Mode* paketini
   (Package Manager > `com.unity.multiplayer.playmode`) kurup sanal oyuncu açın,
   *Join a table > Direct (IP / LAN)*, adres **127.0.0.1**, port **7777**, *Sit down*, sonra *I am ready*.
   Host *Begin* der.
4. Botsuz tek başına fizik testi: *Ayarlar > Kurucu yalnız başlatabilir*.
5. İnternet üzerinden: Direct modunda host'un UDP 7777 portunu yönlendirin (veya VPN/Hamachi/Tailscale),
   ya da projeyi Unity Cloud'a bağlayıp **Online (Relay)** modunu kullanın (aşağıya bakın).

Kontroller: **Sol tık basılı tut** blok çek · **Sağ tık sürükle** etrafa bak · **A/D** yana eğil ·
**W/S veya tekerlek** öne eğil · **F** masaya vur · **C** kart oyna · **TAB basılı tut** gizli görevine bak · **Esc** menü.

**Eller (Hands Over tarzı):** imleç yok, imleç sizin elinizdir. Sıra sizde değilken eliniz masada kendi önünüzde
gezinir (kuleye uzanamaz), gerilim artınca parmaklar masada tıkırdar, **F** ile masaya vurursunuz. Sıra sizdeyken
işaret parmağınız hedeflediğiniz bloğa dokunur, kilitli bloktan geri çekilir, çektiğiniz bloğun ucunu iki parmakla
tutar ve taşırken üstünden kavrar. Tüm oyuncuların elleri ağ üzerinden herkese görünür (`HandSync`, 15 Hz).

**Arayüz:** kağıt, mürekkep ve daktilo. Yazı tipleri `Assets/SecretJenga/Fonts` altında (IM Fell English — OFL,
Special Elite — Apache 2.0). Kağıt/bant/mürekkep sprite'ları `Tools~/generate_ui_textures.py` ile üretilir.

---

## 1. Architecture

Host-client (one player hosts and runs the authoritative simulation) with Unity Netcode for GameObjects 2.x and
Unity Transport. Everything game-wide lives on **one networked session object** (`Prefabs/SJ_Session.prefab`);
blocks are separate networked objects. Local presentation (camera, avatars, atmosphere, UI) is separate and only
reads replicated state.

| Responsibility | Script | Where |
|---|---|---|
| Network session (Direct IP / Relay+Lobby), approval, disconnects | `Networking/NetworkSessionManager` (+ `LobbyManager`, `RelayConnectionManager`, `MultiplayerServiceManager`) | scene: *TheLastBlock Systems* |
| Roster, seats, room settings, phase, round flow, results | `Game/NetworkGameManager` | session prefab |
| Turn order, active/next player, timer, block grab/pull/carry/place, card effects | `Game/TurnManager` | session prefab |
| Tower construction, legal rows, placement slots | `Game/JengaTower` | session prefab |
| Wobble measurement, collapse detection (physics only) | `Game/TowerStabilityChecker` | session prefab |
| Secret task dealing + progress (private) | `Game/SecretTaskManager` + `Data/TaskDefinition` assets | session prefab |
| Card dealing, validation, effects broadcast | `Game/CardManager` + `Data/CardDefinition` assets | session prefab |
| Tension value + synced atmosphere events | `Game/AtmosphereSync` | session prefab |
| One Jenga block (physics on host, replicated pose) | `Game/JengaBlock` | `Prefabs/SJ_Block.prefab` |
| Seated first-person camera | `Game/CameraController` | Main Camera |
| Mouse picking, hover, prompts, placement ghosts | `Game/BlockInteractionController` | Systems |
| Seated placeholder figures and hands | `Game/PlayerAvatars` | Systems |
| Lamp flicker/sway, drone, heartbeat, post FX | `Game/AtmosphereDirector` | Systems |
| Sounds (slide vs. strain loops, 3D one-shots) | `AudioManager` | Systems |
| Menus, lobby, HUD, task panel, card panel, results | `UI/UIManager*.cs` (built in code) | Systems |
| Generates every asset + the scene | `Editor/SecretJengaProjectGenerator` | menu Tools > The Last Block |

The C# namespace (`SecretJenga.*`) and some file names are kept from the previous version so existing asset
references stay valid; the game itself is THE LAST BLOCK.

### Physics authority (and its limits)
* **Only the host simulates** the tower. Each block has a `Rigidbody` + `NetworkRigidbody` + `NetworkTransform`;
  on clients the bodies are kinematic and follow the host's pose (interpolated). Clients can never produce a
  contradictory tower.
* Clients send **intent only** (grab block X at point P, drag point, release, place in slot N) through RPCs. The
  host checks it is that player's turn, that the row is legal and the slot free, then applies forces.
* Pulling is force-driven toward the cursor projected on the block's long axis (`TurnManager.TickDragging`,
  max force 70, max speed 1.6 u/s). Once the block is out it becomes kinematic and is carried to the top; placing
  snaps it into a free slot of the correct orientation and it becomes dynamic again.
* Collapse is never animated: `TowerStabilityChecker` only *detects* fallen blocks (lying on the table, flung
  away, tipped > 55°) after physics made them fall.
* Limitations: clients see the tower with ~100 ms interpolation delay (fine for watching, the active player
  feels a small lag between mouse and block); the host's frame rate drives everyone's physics; if the host quits,
  the session ends for everyone (no host migration); late joining is only allowed in the lobby (reconnecting
  players can return mid-round).

### Tuning values (Inspector / code)
| Value | Default | Where |
|---|---|---|
| Physics step / solver iterations | 0.01 s / 16 + 4 | `GameBootstrap.Awake` |
| Block mass / friction (static, dynamic) | 1 / 0.55, 0.42 | `JengaDims.BlockMass`, `JengaBlock.NormalMaterial` |
| Held-block friction (normal / Steady Grip) | 0.16 / 0.09 | `JengaBlock.SlipperyMaterial`, card asset |
| Linear / angular damping | 0.05 / 0.2 | `JengaBlock.OnNetworkSpawn` |
| Pull force / speed | 70 / 1.6 | `TurnManager.MaxPullForce`, `MaxDragSpeed` |
| Required successful turns | 12 (4–40) | lobby, `GameRules` |
| Tower layers at start | 15 (8–18) | lobby |
| Turn timer | 60 s (Off, 20–120) | lobby |

---

## 2. Packages and project configuration

Already in `Packages/manifest.json`: Netcode for GameObjects 2.13, Input System 1.18, URP 17.3,
Multiplayer Services 2.4 (Relay + Lobby). Unity version: **6000.3.9f1** (the design brief mentioned 2022.3; this
project already runs on Unity 6, which is what the code targets and was compile-checked against).

* **Active Input Handling** must include the *Input System* package (Player Settings).
* `Tools > The Last Block > Generate Project` creates and wires: NetworkManager (Unity Transport, connection
  approval on, scene management off, tick rate 30, prefab list `SJ_NetworkPrefabs`), the session and block
  prefabs, the asset registry `Resources/SecretJengaAssets.asset`, the URP volume, lights and the scene.
* **Direct (IP) mode** needs nothing else. Over the internet the host must forward **UDP** port 7777 (or the port
  chosen on the Host screen) or use a VPN.
* **Online (Relay) mode** is optional and does *not* work automatically: link the project in
  *Edit > Project Settings > Services* to a Unity Cloud project and enable **Relay** and **Lobby** in the Unity
  Dashboard. `Tools > The Last Block > Validate Setup` reports what is missing.

Sounds and textures are generated by scripts in `Tools~` (Python + numpy/scipy/pillow):
`generate_audio.py` (wood), `generate_horror_audio.py` (room, tension, UI), `generate_textures.py`,
`generate_horror_textures.py` (concrete, worn table). The generated files are already in the project.

---

## 3. Scene hierarchy (created by the generator)

```
TheLastBlock.unity
├─ Main Camera            Camera (FOV 52, near 0.05), AudioListener, CameraController, URP post on, SMAA
├─ Room                   TLB_Room prefab: round worn table (MeshCollider), iron pedestal, 5 chairs,
│                          concrete floor (BoxCollider) / walls / ceiling, pipes, steel door
├─ LampRig                swings; children: Cord, Shade, Bulb, Lamp Spot Light (230, range 48, 104°, soft shadows),
│                          Bulb Glow (point 16, range 30)
├─ Cold Rim (far door)    directional 0.05, bluish
├─ Global Volume          TLB_PostProcess: ACES, bloom, vignette 0.4, desaturation, film grain, slight CA
├─ NetworkManager         NetworkManager + UnityTransport
├─ TheLastBlock Systems   GameBootstrap, MultiplayerServiceManager, RelayConnectionManager, LobbyManager,
│                          NetworkSessionManager, AudioManager, EffectsManager, BlockInteractionController,
│                          PlayerAvatars, AtmosphereDirector (lamp/volume references wired), UIManager
├─ Menu Showcase Tower    MenuShowcase (decorative tower in menus / lobby)
└─ EventSystem            EventSystem + InputSystemUIInputModule
```
Spawned at runtime: `SJ_Session` (NetworkObject, TurnManager, JengaTower, SecretTaskManager, CardManager,
TowerStabilityChecker, AtmosphereSync, NetworkGameManager) and `SJ_Block` ×N (NetworkObject, NetworkTransform,
NetworkRigidbody, Rigidbody, BoxCollider 1.5×0.3×0.5, JengaBlock).

Scale: 1 unit ≈ 5 cm. Table top at y = 0, radius 8.2. Seats at 270° + 72°·k, eye 10.2 from the tower, 7.2 above
the table (`SeatLayout`).

---

## 4. Rules implemented

* Join, take one of five chairs. Host presses *Begin* when everyone is ready. Turn order is random.
* Your turn: grab a legal block (not from the top row or the unfinished row below it), pull it out along its
  length, move it over the tower, click a marked free slot. The turn counts when the tower has settled.
* **No turn limit — whoever drops the tower loses.** Play goes round the table until the tower falls (any block
  leaving the tower counts, Second Chance aside). The player whose turn it was loses, everybody else wins.
  Timeouts and turns without a placed block simply pass to the next player. If too few players stay connected the
  round ends without a loser.
* **Bots** (host only): *Play alone vs bots* in the main menu, or *+ seat a bot* in the lobby. `Game/BotBrain` runs on
  the host and drives the same TurnManager entry points as a client RPC (grab, pull, carry, place), plays its card
  now and then and gets nervous when the tower sways.
* **Secret tasks** (one per player, private, revealed at the end): Risk Taker, Steady Hands, Card Collector,
  Pressure Player, Tower Keeper, Careful Builder. Targets are sized for a typical round (`GameRules.TaskTurnHint`)
  so tasks stay achievable; no task needs anyone to fail.
* **Cards** (one random card per player per round, private until played; key **C**):
  * *Steady Grip* — your turn, before the block is out: slower, straighter pull, lower held friction.
  * *Pressure* — your turn: the next player must take the first block they touch on their next turn (touch-move).
  * *Second Chance* — your turn: once, extra time if you would time out before extracting, or retry if the block
    you placed slides off while the rest of the tower stands. It cannot undo a collapse.
* Tasks and cards are `ScriptableObject` assets in `Assets/SecretJenga/Data` — change wording, numbers or
  remove them from the pool in the Inspector. New tasks of an existing kind = duplicate an asset.
  (Asset order is networked: keep the same assets in every build.)

---

## 5. Test procedures and acceptance criteria

Use several builds (*Settings > New identity on every launch* lets several builds run on one PC), or install the
Multiplayer Play Mode package (`com.unity.multiplayer.playmode`, up to 4 virtual players inside the editor).

**Phase 1 — Local tower.** Settings > *Allow the host to start alone*, Host > Direct > Begin.
Expect: 15-layer tower, alternating 90° rows, standing still. Pull blocks, place them on top, repeat until it falls.
✔ Several turns completed; the tower eventually collapses through physics only.

**Phase 2 — First person.** You sit in a chair; RMB look is limited, A/D lean, W/S lean in, middle click
recenters. Hovered legal blocks glow warm, locked ones red with a "Top rows are locked" prompt. Your arms reach
for the block. ✔ You can identify, grab, pull and place without leaving the seat.

**Phase 3 — Multiplayer.** Host + 1 client (then 4 clients = 5 players). Expect: seats spread around the table,
everyone sees the same tower, turn order, active hand and pulled block. Only the active player can move blocks;
clicks of others do nothing. ✔ Two (then five) instances show the same state.

**Phase 4 — Objective and rounds.** Set *Successful turns* to 4 for a quick test. ✔ All players see the same
counter and the same outcome; *Play Another Round* rebuilds the tower and redeals; *Back to Lobby* works.

**Phase 5 — Tasks and cards.** Hold TAB: each player sees only their task; play each card. Expect a toast for
everyone when a card is played, effect indicators under the turn line, Pressure enforced on the next player.
✔ Results screen reveals every task with progress and the cards played.

**Phase 6 — Atmosphere.** Expect a dim room with edges lost in darkness, the lamp swinging slightly more as the
tower grows/wobbles, short synced flickers, distant metal sounds, wood strain when a pull disturbs the tower,
a sting + flicker on collapse. ✔ Tense, but blocks stay readable.

**Phase 7 — Robustness.** Try: clicking during others' turns; grabbing top-row blocks; closing a client mid-turn
(its turn is finished safely and skipped; *Rejoin Last Game* brings it back); letting the timer run out;
collapsing the tower while carrying; repeated rounds. ✔ No stuck state; the round always ends or continues.

### Common problems
* *"Session prefab missing"* / pink materials → run **Generate Project** again.
* Client times out in Direct mode → wrong IP/port, firewall blocking UDP 7777, or host not started.
* *"Version mismatch"* → host and client builds differ (`GameRules.BuildVersion`).
* Online mode error about project linking → link Unity Cloud or use Direct mode.
* Two editor instances get *"already connected"* → use Multiplayer Play Mode (distinct identities) or
  *New identity on every launch*.

---

## 6. Out of scope for this prototype
Multiple rooms, free movement, monsters/combat, classes, customization, progression, cosmetics, matchmaking,
host migration, final character models/animations.


---

## 7. Credits

* Characters: **Universal Animation Library** by Quaternius — CC0 1.0 (https://quaternius.com), dressed as saloon
  patrons by `Tools~/blender_build_cowboy.py` (needs Blender 4.x or the `bpy` Python module + `UAL1.glb`).
* Fonts: IM Fell English (Igino Marini, OFL), Special Elite (Astigmatic, Apache 2.0).
* Everything else (room, textures, sounds, UI art) is generated by the scripts in `Tools~`.
