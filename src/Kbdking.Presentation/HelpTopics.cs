namespace Kbdking.Presentation;

/// <summary>One explanation behind an info (ⓘ) button.</summary>
public sealed record HelpTopic(string Title, string Text);

/// <summary>
/// The detailed explanations shown by the app's info buttons, by topic id. Kept here (not in XAML) so they are
/// written in one place and tests can check that every info button in the views has a text.
/// </summary>
public static class HelpTopics
{
    public static IReadOnlyDictionary<string, HelpTopic> All { get; } = new Dictionary<string, HelpTopic>(StringComparer.Ordinal)
    {
        // ------------------------------------------------------------ editor tabs
        ["tab-keys"] = new("Keys",
            "The keyboard shows what every key types in the selected shift state. Click a key to edit it: each row is one shift state " +
            "(Base, Shift, AltGr, …). Type the character itself, or a code like U+0101; the magnifier finds characters by name.\n\n" +
            "Orange keys are dead keys: they type nothing at first and change the next key (´ then e → é). Grey keys (Shift, Enter, …) " +
            "have no characters; change what they do on Advanced → Scan codes.\n\n" +
            "The box at the bottom types with the layout you are editing, so you can try it before installing."),
        ["tab-deadkeys"] = new("Dead keys",
            "A dead key waits for the next keystroke: dead key + base → result, e.g. ´ + e → é. Each dead key has a list of these " +
            "compositions. A base the list doesn't know types the accent followed by the base.\n\n" +
            "\"Chained\" makes the result another dead key, so two accents combine: dot below, then macron, then r → ṝ.\n\n" +
            "\"Add with compositions\" creates a dead key for a common accent with all Latin letters it combines with. To put a dead key " +
            "on the keyboard, select a key on the Keys tab and tick \"Dead key\" in a row."),
        ["tab-compose"] = new("Compose key",
            "Compose is a key you press before a short, memorable sequence: Compose . r → ṛ, Compose - - → –, Compose o c → ©. " +
            "Unlike AltGr you don't hold it, and there are no limits on how many characters you can reach.\n\n" +
            "kbdking builds the sequences into the layout as hidden chained dead keys, so they work in every program without " +
            "WinCompose or AutoHotkey running. Sequences are written as the characters you type after Compose.\n\n" +
            "Results must be a single character (no emoji). A sequence can't also be the beginning of a longer one; such conflicts " +
            "appear under Warnings. Import an .XCompose file to reuse sequences from Linux or WinCompose."),
        ["tab-assistant"] = new("Placement assistant",
            "Enter the characters you need (or paste a text in the language) and press Suggest. The assistant keeps everything the " +
            "layout already has and proposes:\n" +
            "• a dead key for an accent that many of your characters share, on a key that looks like it (dot below on the . key),\n" +
            "• chained dead keys for letters with two accents (ṝ = dot below + macron),\n" +
            "• a key with the combining accent for letters Unicode has no single character for, typed after the letter " +
            "(r, then the ring below → r̥),\n" +
            "• direct AltGr positions on the base letter for the rest (AltGr+n → ñ).\n\n" +
            "Untick what you don't want and press Apply. Everything can be undone."),
        ["tab-analysis"] = new("Text analysis",
            "Paste a text you type often. kbdking finds the cheapest way to type every character with this layout and adds up an " +
            "effort score: about 1.0 per character means mostly home-row keys without modifiers; Shift, AltGr, far keys and dead keys " +
            "cost more.\n\n" +
            "The heatmap shows which keys the text uses most. \"Not typeable\" lists characters the layout can't produce at all; " +
            "\"Place missing characters\" hands them to the assistant. \"Compare\" runs the same text against another layout file."),
        ["tab-tests"] = new("Layout tests",
            "A test says: these keys must type this text. kbdking checks all tests after every change and refuses to build or " +
            "install a layout with a failing test, so later edits can't silently break something.\n\n" +
            "Write keys separated by spaces, modifiers joined with +: \"AltGr+. t\" (dot-below dead key, then t), \"Shift+AltGr+a\", " +
            "\"Compose . r\", \"LayerA+a\". A character means the key that types it (\"a\"); \"PLUS\" is the + key.\n\n" +
            "\"Add tests for what the layout types now\" records the current state of every special character as tests."),
        ["tab-compare"] = new("Compare",
            "Lists every difference between this layout and another layout file or the version saved on disk: changed characters, " +
            "dead keys, shift states, scan codes, names and settings. Changed keys are marked green on the Keys tab and in the " +
            "picture. The comparison updates while you keep editing."),
        ["tab-shiftstates"] = new("Shift states and layer keys",
            "Every shift state (Base, Shift, AltGr = Ctrl+Alt, …) is a column of characters. You can add columns, also with Kana, or " +
            "remove them with their characters.\n\n" +
            "Layer keys give you extra layers: hold the layer key (for example Caps Lock or the < > key) and the keyboard types the " +
            "layer's characters, like with Shift. Up to three layers (A, B, C), each also with Shift. They are built into the layout, " +
            "so nothing runs in the background. Layers only work while the key is held; there is no lock."),
        ["tab-scancodes"] = new("Scan codes",
            "Each physical key sends a scan code; the layout turns it into a virtual key (VK), which programs use for shortcuts, and " +
            "the VK into characters. Changing the virtual key of a scan code moves a key: give scan code 15 the virtual key Y and the " +
            "key next to T becomes Y.\n\n" +
            "Write targets as virtual key names (Y, OEM_8, RMENU) with flags if needed (|EXT for extended keys), or NONE to switch a " +
            "key off. Usually you don't need this tab: the Keys tab, layer keys and the shortcut setting cover the common cases."),
        ["tab-keynames"] = new("Key names",
            "The names Windows shows for keys that don't type characters: in shortcut settings, some games and accessibility tools " +
            "(for example \"Strg\" or \"Ctrl\"). They don't change what the keys do. \"Use standard names\" fills in English or German names."),
        ["tab-ligatures"] = new("Ligatures",
            "A ligature is a key that types several characters at once: \"ng\", or a letter plus a combining accent (r + U+0323 gives ṛ " +
            "in decomposed form). Windows allows up to 16 UTF-16 units per key; MSKLC only accepted 4.\n\n" +
            "Ligatures can't be the result of a dead key, and some old programs handle them poorly; prefer single precomposed " +
            "characters (ṛ = U+1E5B) where they exist."),
        ["tab-layout"] = new("Layout settings",
            "Name, file name, language and version of the layout, and global behaviour such as AltGr and where shortcuts are. These " +
            "settings decide how the layout appears in Windows."),
        ["tab-problems"] = new("Problems",
            "kbdking checks the layout after every change. Errors (red) prevent building and installing: missing names, invalid " +
            "characters, failing tests, impossible combinations. Warnings (yellow) point out things that work but may surprise you, " +
            "for example AltGr combinations that some programs use as Ctrl+Alt shortcuts. The code (KS…) identifies each check."),

        // ------------------------------------------------------------ keys tab
        ["shift-state"] = new("Shift state",
            "Which modifier combination the keyboard picture shows. AltGr is the right Alt key; Windows treats it as Ctrl+Alt. " +
            "Ctrl states mostly produce control codes and are rarely changed. Layer states appear here once you add a layer key.\n\n" +
            "Auto follows the keys you hold: hold Shift, AltGr, Shift+AltGr or a layer key and the keyboard shows that state " +
            "until you let go. The light grey keys are the home row, where the fingers rest."),
        ["keyboard-shape"] = new("Keyboard shape",
            "Only changes the picture, not the layout. ISO is the European keyboard (tall Enter, extra < > key), ANSI the US keyboard, " +
            "JIS the Japanese and ABNT the Brazilian one with their extra keys."),
        ["key-editor"] = new("Editing a key",
            "Type what the key should produce in each shift state: a character, several characters (a ligature) or a code like " +
            "U+0101. Leave a row empty for nothing.\n\n" +
            "\"Dead key\" turns the character into an accent that waits for the next key; define what it combines with on the Dead " +
            "keys tab. \"Caps Lock acts like Shift\" is right for letters; untick it for keys with punctuation. \"…also with AltGr\" " +
            "lets Caps Lock capitalise AltGr letters too (AltGr+a → Ā with Caps Lock)."),
        ["preview"] = new("Try it",
            "Click into the box and type. kbdking intercepts your keystrokes and types them with the layout you are editing, " +
            "including dead keys, compose sequences and layer keys, so you can test before installing. Ctrl shortcuts are passed on " +
            "to the app. It doesn't change the layout Windows uses elsewhere."),

        // ------------------------------------------------------------ compose / assistant / analysis
        ["compose-key"] = new("Which key is Compose",
            "The key you press to start a sequence. The Menu key (between AltGr and right Ctrl) is unused on most keyboards. " +
            "Right Windows works too and keeps the left Windows key. Caps Lock is a popular choice if you don't need Caps Lock.\n\n" +
            "The Copilot key sends Windows + Shift + F23, and Windows may open Copilot with it before the layout sees the key. " +
            "Then set Settings > Bluetooth & devices > Keyboard > Copilot key to \"Right Ctrl\" (Windows 11 update of October 2026 or " +
            "later) and choose Right Ctrl as the compose key; \"Context menu\" with the Menu key works the same way. Windows rolls that " +
            "setting out gradually; if your PC only offers Search, Copilot and Open an app, use another compose key for now.\n\n" +
            "The chosen key loses its old function in this layout only."),
        ["compose-accent"] = new("Shown while waiting",
            "Windows needs a character for the waiting state of a dead key. Some programs show it briefly while you type the " +
            "sequence. The default ⎄ (U+2384, COMPOSITION SYMBOL) is never typed; pick a character the layout doesn't use elsewhere."),
        ["assistant-threshold"] = new("Characters for an own dead key",
            "An accent gets its own dead key once at least this many of your characters use it. Below that, the characters go " +
            "directly on AltGr positions instead. Higher numbers mean fewer dead keys and more AltGr positions."),
        ["analysis-effort"] = new("Effort score",
            "A simple, explainable estimate: every keystroke costs by row and finger (home row cheapest, number row and far corners " +
            "most), plus Shift 0.6, AltGr 1.0, Ctrl 1.0 and other layers 1.5. A dead key sequence costs the sum of its keystrokes. " +
            "The score is the average per character of your text. It compares layouts on the same text; it isn't an absolute measure."),

        // ------------------------------------------------------------ layout tab
        ["dll-name"] = new("DLL name",
            "Windows keyboard layouts are files in C:\\Windows\\System32, named like kbdgr.dll. This is the file name without .dll: " +
            "1–8 letters, digits or underscores, and it must not clash with another layout. Keep it once you've installed the layout, " +
            "so updates replace the same file."),
        ["language"] = new("Language",
            "The language the layout is listed under in Windows (a tag like de-AT, en-US, sa-IN). The taskbar shows the language, not " +
            "the layout name, which is why all layouts under German show \"DEU\". Programs such as Word also take the spell-checking " +
            "language from it.\n\n" +
            "To tell layouts apart in the taskbar, use another region of the same language (de-LU instead of de-AT keeps German " +
            "spelling) or another language. A new language gives the layout a new ID: uninstall the old one afterwards."),
        ["layout-version"] = new("Version",
            "Your version number for the layout, like 1.0 or 1.2.3 (each part at most 65535). It is stored in the DLL and shown by " +
            "installers; raise it when you share an updated layout."),
        ["altgr"] = new("Right Alt acts as AltGr",
            "When on, the right Alt key works as AltGr (Windows sends Ctrl+Alt) and selects the AltGr characters. Turn it off only for " +
            "layouts without AltGr characters, where right Alt should be a normal Alt."),
        ["shortcuts"] = new("Where shortcuts are",
            "Programs recognise shortcuts like Ctrl+C, Ctrl+V and Ctrl+Z by virtual key, not by the character a key types. Normally " +
            "they follow the letters: on Colemak or Dvorak, Ctrl+C moves to wherever c is.\n\n" +
            "\"Stay at QWERTY positions\" (or QWERTZ, AZERTY) keeps the shortcuts under the same fingers as on a standard keyboard, " +
            "while every key keeps typing exactly the same characters. Some web apps look at characters instead and still follow " +
            "the letters."),
        ["layout-id"] = new("Layout ID",
            "The identifier Windows uses for the installed layout (KLID, for example A0000C07: the last four digits are the language). " +
            "kbdking assigns it on the first install and stores it in the file, so installing again updates the same layout instead " +
            "of adding a second one. Windows loads a layout once per sign-in, so after an update sign out and back in before " +
            "testing it in other programs."),

        // ------------------------------------------------------------ install
        ["test-install"] = new("Test install",
            "Installs the layout for a limited time so you can try it in real programs. A scheduled task removes it again when the time " +
            "is up, even if kbdking is closed or the PC restarts. Programs that were already open may need a restart to see a new " +
            "layout. To keep it, press Keep on the Layouts page."),

        // ------------------------------------------------------------ layouts page
        ["layouts-page"] = new("Keyboard layouts on this PC",
            "Every layout registered on this PC, the ones in your switcher first. The switch adds it to or removes it from your Win+Space list; that needs no " +
            "administrator rights and deletes nothing.\n\n" +
            "\"Windows\" layouts come with Windows and can only be switched off. \"Custom\" layouts were installed by kbdking: " +
            "Uninstall removes their registry entry and files completely (you see the list first). \"Custom (test)\" layouts are test " +
            "installs that remove themselves; Keep makes them permanent.\n\n" +
            "\"Edit a copy\" opens any layout in the editor as a starting point; the copy gets its own name and ID."),
        ["layouts-repair"] = new("Repair",
            "Windows feature updates sometimes remove custom layouts. kbdking keeps a copy of every layout it installs and Repair " +
            "puts back what is missing. It also removes registrations whose files are gone."),
    };

    /// <summary>The topic, or a short placeholder if an id is unknown (never throws in the UI).</summary>
    public static HelpTopic Get(string? id) =>
        id is not null && All.TryGetValue(id, out var topic) ? topic : new HelpTopic("Help", "No explanation available.");
}
