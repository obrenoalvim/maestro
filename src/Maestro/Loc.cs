using System.ComponentModel;
using System.Globalization;

namespace Maestro;

public sealed record LocText(string Key, params object[] Args)
{
    public override string ToString() => Args.Length == 0 ? Loc.T(Key) : Loc.F(Key, Args);
}

public sealed class Loc : INotifyPropertyChanged
{
    public const string Automatic = "auto";
    public const string English = "en";
    public const string Portuguese = "pt";

    private static readonly Dictionary<string, (string En, string Pt)> Table = new()
    {
        ["headlineNormal"] = ("Maestro in normal mode", "Maestro em modo normal"),
        ["headlineGame"] = ("Maestro in game mode ({0})", "Maestro em modo jogo ({0})"),
        ["headlinePaused"] = ("Maestro paused", "Maestro pausado"),
        ["headlineStarting"] = ("Maestro starting", "Maestro iniciando"),
        ["unknownGame"] = ("game", "jogo"),

        ["trayOpen"] = ("Open Maestro", "Abrir o Maestro"),
        ["trayHistory"] = ("What I did last", "O que fiz por último"),
        ["trayNothing"] = ("Nothing yet", "Nada até agora"),
        ["trayPause"] = ("Pause", "Pausar"),
        ["trayFps"] = ("Show FPS on screen (Ctrl+Alt+F)", "Mostrar FPS na tela (Ctrl+Alt+F)"),
        ["trayStartup"] = ("Start with Windows", "Iniciar com o Windows"),
        ["trayConfigFault"] = ("The settings file has an error. Using the last one that worked", "A configuração tem um erro. Estou usando a última que funcionava"),
        ["trayExit"] = ("Exit", "Sair"),
        ["greetTitle"] = ("Maestro is already running", "O Maestro já está ligado"),
        ["greetText"] = ("{0}. The menu is on the icon next to the clock.", "{0}. O menu fica no ícone perto do relógio."),
        ["updateNotice"] = ("Maestro {0} is out. Download it from the tray menu or the General page.", "Saiu o Maestro {0}. Baixe pelo menu da bandeja ou na página Geral."),
        ["updateButton"] = ("Download Maestro {0}", "Baixar o Maestro {0}"),

        ["noteQbitStale"] = ("Released qBittorrent, which was left slowed down", "Liberei o qBittorrent, que tinha ficado freado"),
        ["noteJournal"] = ("Put {0} back to normal; it was left changed", "Devolvi {0} ao normal, que tinha ficado alterado"),
        ["noteGameOpen"] = ("You opened {0}", "Você abriu {0}"),
        ["noteGameClosed"] = ("You closed {0}", "Você fechou {0}"),
        ["noteQbitSlow"] = ("Slowed qBittorrent until you close the game", "Freei o qBittorrent até você fechar o jogo"),
        ["noteQbitFree"] = ("Released qBittorrent", "Liberei o qBittorrent"),
        ["noteHigh"] = ("Gave {0} high priority", "Dei prioridade alta para {0}"),
        ["noteHighMany"] = ("Gave {0} high priority ({1} processes)", "Dei prioridade alta para {0} ({1} processos)"),
        ["noteRestrain"] = ("Held back {0}: {1}", "Segurei {0}: {1}"),
        ["noteRelease"] = ("Let go of {0}: {1}", "Soltei {0}: {1}"),
        ["noteBoost"] = ("Sped up {0}: {1}", "Acelerei {0}: {1}"),
        ["noteUnboost"] = ("Put {0} back to normal: {1}", "Voltei {0} ao normal: {1}"),
        ["noteMedalOpen"] = ("Started Medal hidden to record the game", "Abri o Medal escondido para gravar o jogo"),
        ["noteMedalClose"] = ("Closed Medal: no game for {0} min", "Fechei o Medal: sem jogo há {0} min"),

        ["whyGame"] = ("it is the game", "é o jogo"),
        ["whyGameOpened"] = ("you opened a game", "você abriu um jogo"),
        ["whyTransferDone"] = ("the transfer ended", "a transferência acabou"),
        ["whyFront"] = ("you brought its window to the front", "você abriu a janela dele"),
        ["whyCalm"] = ("it calmed down", "parou de pesar"),
        ["whyGameRunning"] = ("a game is running", "tem jogo aberto"),
        ["whyTransfer"] = ("moving {0:0} MB/s", "transferindo {0:0} MB/s"),
        ["whyCpu"] = ("used {0:0}% of the CPU in the background", "usava {0:0}% do processador em segundo plano"),

        ["subtitle"] = ("Adapts Windows to what you are doing.", "Ajusta o Windows ao que você está fazendo."),
        ["close"] = ("Close (Esc)", "Fechar (Esc)"),
        ["navHome"] = ("Home", "Início"),
        ["navFps"] = ("FPS test", "Teste de FPS"),
        ["navGames"] = ("Games", "Jogos"),
        ["navApps"] = ("Medal and qBittorrent", "Medal e qBittorrent"),
        ["navGeneral"] = ("General", "Geral"),

        ["homeTitle"] = ("RIGHT NOW", "AGORA"),
        ["running"] = ("Maestro on", "Maestro ligado"),
        ["runningDesc"] = ("Turn it off to compare. Everything it changed goes back to normal at once.", "Desligue para comparar. Tudo o que ele mudou volta ao normal na hora."),
        ["historyTitle"] = ("WHAT I DID LAST", "O QUE FIZ POR ÚLTIMO"),
        ["historyEmpty"] = ("Nothing yet. Open a game or leave something heavy running in the background.", "Nada até agora. Abra um jogo ou deixe algo pesado rodando em segundo plano."),

        ["fpsTitle"] = ("FPS ON SCREEN", "FPS NA TELA"),
        ["showFps"] = ("Show the FPS of the window in front", "Mostrar o FPS da janela da frente"),
        ["showFpsDesc"] = ("Also toggles with Ctrl+Alt+F. It shows in the corner of the game's screen. Exclusive fullscreen hides it; use borderless window.",
            "Também liga e desliga com Ctrl+Alt+F. Aparece no canto da tela do jogo. Tela cheia exclusiva esconde o contador; use janela sem borda."),
        ["permTitle"] = ("PERMISSION", "PERMISSÃO"),
        ["permMissing"] = ("Windows only lets an app read another program's frames with admin rights or with your account in the Performance Log Users group. Allow it once and sign out and back in: it never asks again. Until then, each measurement asks for admin.",
            "O Windows só deixa ler os quadros de outro programa com administrador ou com a sua conta no grupo Usuários de log de desempenho. Permita uma vez e saia e entre na sua conta: depois disso não pede mais. Até lá, cada medição pede administrador."),
        ["permPending"] = ("Permission granted. Sign out and back in to Windows for it to apply. Until then, each measurement asks for admin.",
            "Permissão dada. Saia e entre na sua conta do Windows para valer. Até lá, cada medição pede administrador."),
        ["permReady"] = ("Ready. Maestro measures FPS without asking for anything.", "Pronto. O Maestro mede FPS sem pedir nada."),
        ["permGrant"] = ("Allow once", "Permitir uma vez"),
        ["permFailed"] = ("Windows did not grant the permission. Try again and accept the admin prompt.", "O Windows não deu a permissão. Tente de novo e aceite o pedido de administrador."),
        ["fpsDownloading"] = ("Downloading the meter (PresentMon, by Intel)...", "Baixando o medidor (PresentMon, da Intel)..."),
        ["fpsDownloadFailed"] = ("Could not download PresentMon: {0}", "Não consegui baixar o PresentMon: {0}"),
        ["fpsRefused"] = ("The admin prompt was declined, so there is no FPS reading.", "O pedido de administrador foi recusado, então não há leitura de FPS."),
        ["overlayNoFrames"] = ("no frames", "sem quadros"),
        ["overlayLow"] = ("1% low {0:0}", "1% baixo {0:0}"),

        ["compareTitle"] = ("COMPARE WITH AND WITHOUT MAESTRO", "COMPARAR COM E SEM O MAESTRO"),
        ["compareDesc"] = ("Leave the game on a still or repeatable scene. Maestro measures it first turned off, then on.",
            "Deixe o jogo numa cena parada ou repetível. O Maestro mede primeiro desligado e depois ligado."),
        ["compareSeconds"] = ("Length of each measurement", "Duração de cada medição"),
        ["seconds"] = ("{0} s", "{0} s"),
        ["simulateLoad"] = ("Simulate a heavy program in the background", "Simular um programa pesado em segundo plano"),
        ["simulateLoadDesc"] = ("Starts a process that keeps half of the cores busy, like a download, a build or a browser full of tabs. That is where Maestro makes a difference.",
            "Abre um processo que ocupa metade dos núcleos, como um download, uma compilação ou um navegador cheio de abas. É onde o Maestro faz diferença."),
        ["compareStart"] = ("Compare", "Comparar"),
        ["compareCancel"] = ("Cancel", "Cancelar"),
        ["phaseCountdown"] = ("Go back to the game: starting in {0} s", "Volte para o jogo: começa em {0} s"),
        ["phaseWithout"] = ("Without Maestro", "Sem Maestro"),
        ["phaseWith"] = ("With Maestro", "Com Maestro"),
        ["phaseSettle"] = ("{0}: letting it settle", "{0}: esperando estabilizar"),
        ["phaseMeasure"] = ("{0}: {1} s left", "{0}: faltam {1} s"),
        ["compareNoFrames"] = ("No frames came from {0}. Keep the game in front during the measurement.", "Não chegaram quadros de {0}. Deixe o jogo na frente durante a medição."),
        ["compareCanceled"] = ("Comparison canceled.", "Comparação cancelada."),
        ["resultAvg"] = ("Average FPS", "FPS médio"),
        ["resultLow"] = ("1% low", "1% mais baixos"),
        ["resultFrame"] = ("Worst frame time (p99)", "Pior tempo de quadro (p99)"),
        ["resultDelta"] = ("Average {0:+0.0;-0.0;0} FPS ({1:+0;-0;0}%), 1% low {2:+0.0;-0.0;0} FPS", "Média {0:+0.0;-0.0;0} FPS ({1:+0;-0;0}%), 1% mais baixos {2:+0.0;-0.0;0} FPS"),
        ["resultNote"] = ("Differences under 2% are within the normal variation between two runs.", "Diferenças abaixo de 2% ficam dentro da variação normal entre duas medições."),
        ["resultLine"] = ("{0:g} · {1} · {2}", "{0:g} · {1} · {2}"),
        ["previousTitle"] = ("PREVIOUS COMPARISONS", "COMPARAÇÕES ANTERIORES"),
        ["loadTag"] = ("with load", "com carga"),

        ["gamesTitle"] = ("GAMES", "JOGOS"),
        ["gamesDesc"] = ("Maestro follows Windows: the Game Bar, the games Windows has seen and the ones you marked by hand. Add here what it does not recognize. One process name per line, without .exe.",
            "O Maestro segue o Windows: a Game Bar, os jogos que o Windows já viu e os que você marcou à mão. Ponha aqui o que ele não reconhecer. Um nome de processo por linha, sem .exe."),
        ["extraGames"] = ("Also count as games", "Também contam como jogo"),
        ["neverRestrain"] = ("Never hold back", "Nunca segurar"),
        ["slowDuringGames"] = ("Hold back while a game runs", "Segurar enquanto houver jogo"),

        ["medalManage"] = ("Start Medal when a game starts and close it afterwards", "Abrir o Medal quando um jogo começa e fechar depois"),
        ["medalPath"] = ("Path to Medal.exe", "Caminho do Medal.exe"),
        ["medalClose"] = ("Minutes without a game before closing it", "Minutos sem jogo antes de fechar"),
        ["qbitDesc"] = ("To slow its bandwidth during games, turn on qBittorrent's Web UI and tick Bypass authentication for clients on localhost.",
            "Para frear a banda durante o jogo, ligue a Interface Web do qBittorrent e marque Ignorar autenticação para clientes no localhost."),
        ["qbitUrl"] = ("Web UI address", "Endereço da Interface Web"),
        ["qbitTest"] = ("Test", "Testar"),
        ["qbitOk"] = ("Connected. Alternative speed limit is {0}.", "Conectado. O limite alternativo está {0}."),
        ["qbitFail"] = ("No answer. Is the Web UI on?", "Sem resposta. A Interface Web está ligada?"),
        ["on"] = ("on", "ligado"),
        ["off"] = ("off", "desligado"),

        ["generalTitle"] = ("GENERAL", "GERAL"),
        ["language"] = ("Language", "Idioma"),
        ["langAuto"] = ("Automatic", "Automático"),
        ["startWithWindows"] = ("Start with Windows", "Iniciar com o Windows"),
        ["checkUpdates"] = ("Tell me when a new version is out", "Avisar quando sair versão nova"),
        ["checkUpdatesDesc"] = ("Asks GitHub for the latest release every few hours. Nothing is downloaded or installed by itself.",
            "Pergunta ao GitHub pela última versão a cada poucas horas. Nada é baixado nem instalado sozinho."),
        ["version"] = ("Version {0}", "Versão {0}"),
        ["openConfig"] = ("Open the settings file", "Abrir o arquivo de configuração"),
        ["openLogs"] = ("Open the full history", "Abrir o histórico completo"),
        ["github"] = ("Source on GitHub", "Código no GitHub"),
        ["privacy"] = ("No telemetry. Maestro only goes online to download PresentMon when you turn on the FPS meter and, if you allow it, to check for a new version.",
            "Sem telemetria. O Maestro só acessa a internet para baixar o PresentMon quando você liga o contador de FPS e, se você deixar, para ver se saiu versão nova."),
    };

    private string language = English;

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language => language;

    public CultureInfo Culture => CultureInfo.GetCultureInfo(language == Portuguese ? "pt-BR" : "en-US");

    public string this[string key] =>
        Table.TryGetValue(key, out var pair) ? (language == Portuguese ? pair.Pt : pair.En) : key;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object[] args) => string.Format(Instance.Culture, Instance[key], args);

    public static IReadOnlyCollection<string> Keys => Table.Keys;

    public static string Resolve(string? setting) => setting switch
    {
        Portuguese => Portuguese,
        English => English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Portuguese ? Portuguese : English,
    };

    public void Set(string? setting)
    {
        var next = Resolve(setting);
        if (next == language) return;
        language = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
