using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga
{
    public enum Language : byte { English = 0, Turkish = 1 }

    /// <summary>
    /// Tiny two-language localisation (English / Türkçe).
    ///
    ///  * UI text:  L.T("English", "Türkçe") right where the text is written. Changing the language rebuilds the UI.
    ///  * Server messages travel as tokens ("@key|arg|arg") so every client shows them in ITS OWN language:
    ///    the host calls L.Msg("key", args), receivers call L.Resolve(text) before showing it.
    ///  * Task / card wording is looked up by the definition id (assets keep the English text).
    /// </summary>
    public static class L
    {
        const string KLang = "tlb_language";
        public static event Action Changed;

        static int _cached = -1;

        public static Language Current
        {
            get
            {
                if (_cached < 0)
                {
                    int def = Application.systemLanguage == SystemLanguage.Turkish ? 1 : 0;
                    _cached = PlayerPrefs.GetInt(KLang, def);
                }
                return (Language)_cached;
            }
            set
            {
                if ((int)value == _cached) return;
                _cached = (int)value;
                PlayerPrefs.SetInt(KLang, _cached);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool TR => Current == Language.Turkish;

        /// <summary>Pick the text for the current language.</summary>
        public static string T(string en, string tr) => TR ? tr : en;

        /// <summary>Upper-case that respects Turkish i/İ and ı/I.</summary>
        public static string Upper(string s) => string.IsNullOrEmpty(s) ? s :
            (TR ? s.Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant() : s.ToUpperInvariant());

        // ------------------------------------------------------------------ Network message tokens
        /// <summary>Build a language-neutral message on the host: "@key|arg0|arg1".</summary>
        public static string Msg(string key, params object[] args)
        {
            var s = "@" + key;
            if (args != null) foreach (var a in args) s += "|" + (a?.ToString() ?? "").Replace("|", "/");
            return s;
        }

        /// <summary>Turn a received message into the local language. Plain strings pass through untouched.</summary>
        public static string Resolve(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return msg;
            if (msg.IndexOf('\n') >= 0)
            {
                var lines = msg.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = Resolve(lines[i]);
                return string.Join("\n", lines);
            }
            if (msg[0] != '@') return msg;
            var parts = msg.Substring(1).Split('|');
            if (!Messages.TryGetValue(parts[0], out var pair)) return msg.Substring(1);
            var fmt = TR ? pair.tr : pair.en;
            var args = new object[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++) args[i - 1] = parts[i];
            try { return string.Format(fmt, args); } catch (FormatException) { return fmt; }
        }

        static readonly Dictionary<string, (string en, string tr)> Messages = new Dictionary<string, (string, string)>
        {
            // roster / room
            { "back", ("{0} is back at the table.", "{0} masaya geri döndü.") },
            { "sits", ("{0} takes a seat.", "{0} masaya oturdu.") },
            { "left", ("{0} left the table.", "{0} masadan kalktı.") },
            { "lost_conn", ("{0} lost connection. Their turns are skipped until they return.", "{0} bağlantıyı kaybetti. Dönene kadar sırası atlanacak.") },
            { "bot_join", ("{0} pulls up a chair.", "{0} bir sandalye çekti.") },
            { "bot_left", ("{0} leaves the room.", "{0} odadan çıktı.") },
            { "already_running", ("A round is already running.", "Zaten bir el oynanıyor.") },
            { "need_two", ("Need at least 2 players at the table (add a bot to play alone).", "Masada en az 2 oyuncu gerekli (tek başına oynamak için bot ekle).") },
            { "wait_ready", ("Waiting for everyone to be ready.", "Herkesin hazır olması bekleniyor.") },
            { "need_two_back", ("Need at least 2 connected players. Returning to the lobby.", "En az 2 bağlı oyuncu gerekli. Lobiye dönülüyor.") },
            { "table_full", ("The table is full.", "Masa dolu.") },
            { "already_conn", ("This player is already connected to the room.", "Bu oyuncu zaten odaya bağlı.") },
            { "already_started", ("The round has already started.", "El çoktan başladı.") },
            // turns
            { "ran_out", ("{0} ran out of time.", "{0} süresini doldurdu.") },
            { "ran_out_penalty", ("{0} ran out of time without taking a block ({1} point).", "{0} blok almadan süresini doldurdu ({1} puan).") },
            { "not_count", ("{0}'s turn does not count.", "{0} bu turu boşa geçirdi.") },
            { "pressure_on_you", ("Pressure is on you: you must take the first block you touch.", "Baskı sende: ilk dokunduğun bloğu almak zorundasın.") },
            { "pressure_touched", ("Pressure: you must take the block you touched first.", "Baskı: ilk dokunduğun bloğu almalısın.") },
            { "slot_taken", ("That spot is taken. Place the block in a free slot on top.", "Orası dolu. Bloğu üstteki boş bir yere koy.") },
            { "sc_time", ("Second Chance: {0} gets {1} more seconds.", "İkinci Şans: {0} {1} saniye daha kazandı.") },
            { "sc_catch", ("Second Chance: {0} catches the falling block.", "İkinci Şans: {0} düşen bloğu yakaladı.") },
            { "not_top", ("That block is not part of the tower.", "Bu blok kulenin parçası değil.") },
            { "not_top_layer", ("You can't take blocks from the top layers.", "En üst katlardan blok alınamaz.") },
            // round end
            { "fell", ("The tower fell on {0}'s turn after {1} good turns. {0} loses.", "Kule {1} başarılı turdan sonra {0} oynarken yıkıldı. {0} kaybetti.") },
            { "fell_nobody", ("The tower fell.", "Kule yıkıldı.") },
            { "abandoned", ("Not enough players left at the table. Nobody loses.", "Masada yeterli oyuncu kalmadı. Kimse kaybetmedi.") },
            // cards
            { "card_steady", ("{0} plays Steady Grip.", "{0} Sağlam Tutuş oynadı.") },
            { "card_pressure", ("{0} plays Pressure on {1}.", "{0}, {1} üzerine Baskı oynadı.") },
            { "card_second", ("{0} keeps a Second Chance ready.", "{0} bir İkinci Şans hazırda tutuyor.") },
            { "card_nobody", ("There is nobody to pressure.", "Baskı yapılacak kimse yok.") },
            { "card_cant", ("You can't play that card right now.", "Bu kartı şu an oynayamazsın.") },
            { "no_cards", ("No cards were played.", "Hiç kart oynanmadı.") },
        };

        // ------------------------------------------------------------------ Task / card wording (by definition id)
        static readonly Dictionary<string, (string title, string desc)> TaskTR = new Dictionary<string, (string, string)>
        {
            { "risk_taker", ("Gözü Kara", "En alttaki {rows} sıradan {target} blok çek ve başarıyla üste koy.") },
            { "steady_hands", ("Sağlam Eller", "Kendi turlarından art arda {target} tanesini kule %{thresholdNum} üstü sallanmadan bitir.") },
            { "card_collector", ("Kart Ustası", "Özel kartını oyna, sonra ardından {target} başarılı tur tamamla.") },
            { "pressure_player", ("Baskıcı", "Başka bir oyuncunun turunu etkileyen bir kart oyna. (Sana bir tane garanti verilir.)") },
            { "tower_keeper", ("Kule Bekçisi", "Masanın toplam {milestone} başarılı tura ulaşmasına yardım et (en az biri senin olsun) ve kule asla senin turunda yıkılmasın.") },
            { "careful_builder", ("Titiz Usta", "Diğer blokları {tolerance} fazla oynatmadan {target} bloğu üste yerleştir.") },
        };

        static readonly Dictionary<string, (string title, string desc, string duration)> CardTR = new Dictionary<string, (string, string, string)>
        {
            { "steady_grip", ("Sağlam Tutuş", "Turunda, blok çıkmadan önce oyna. Çekişin yavaşlar, düzleşir ve blok daha kolay kayar. Kararı yine fizik verir.", "Blok çıkana kadar") },
            { "pressure", ("Baskı", "Turunda oyna. Sıradaki oyuncu bir sonraki turunda ilk dokunduğu bloğu almak zorunda kalır.", "Sıradakinin turu") },
            { "second_chance", ("İkinci Şans", "Turunda oyna. Bir kez: süren biterse ek süre, ya da koyduğun blok ayakta duran kuleden kayarsa tekrar dene. Yıkılışı kurtarmaz.", "Bu tur") },
        };

        public static string TaskTitle(Game.TaskDefinition d) => d == null ? "" : (TR && TaskTR.TryGetValue(d.id, out var t) ? t.title : d.title);
        public static string TaskDescriptionTemplate(Game.TaskDefinition d) => d == null ? "" : (TR && TaskTR.TryGetValue(d.id, out var t) ? t.desc : d.description);
        public static string CardTitle(Game.CardDefinition d) => d == null ? "" : (TR && CardTR.TryGetValue(d.id, out var c) ? c.title : d.title);
        public static string CardDescription(Game.CardDefinition d) => d == null ? "" : (TR && CardTR.TryGetValue(d.id, out var c) ? c.desc : d.description);
        public static string CardDuration(Game.CardDefinition d) => d == null ? "" : (TR && CardTR.TryGetValue(d.id, out var c) ? c.duration : d.durationText);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _cached = -1; Changed = null; }
    }
}
