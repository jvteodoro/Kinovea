namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Small native-shell string catalog for the currently supported UI languages.</summary>
public sealed class PortableDesktopStrings(string culture)
{
    private readonly bool portuguese = culture.Equals("pt-BR", StringComparison.OrdinalIgnoreCase);

    public string File => portuguese ? "Arquivo" : "File";
    public string OpenImage => portuguese ? "Abrir imagem ou vídeo…" : "Open image or video…";
    public string Close => portuguese ? "Fechar" : "Close";
    public string Preferences => portuguese ? "Preferências…" : "Preferences…";
    public string Capture => portuguese ? "Captura" : "Capture";
    public string SingleCapture => portuguese ? "Captura simples…" : "Single capture…";
    public string DualCapture => portuguese ? "Captura dupla…" : "Dual capture…";
    public string Workspace => portuguese ? "Área de trabalho" : "Workspace";
    public string SaveWorkspace => portuguese ? "Salvar área ativa" : "Save active workspace";
    public string PreferencesTitle => portuguese ? "Preferências do Kinovea" : "Kinovea Preferences";
    public string Culture => portuguese ? "Idioma" : "Language";
    public string Theme => portuguese ? "Tema" : "Theme";
    public string OpenShortcut => portuguese ? "Atalho para abrir" : "Open shortcut";
    public string CaptureInterval => portuguese ? "Intervalo da fonte sintética (ms)" : "Synthetic source interval (ms)";
    public string Save => portuguese ? "Salvar" : "Save";
    public string Cancel => portuguese ? "Cancelar" : "Cancel";
    public string CaptureWindowTitle(bool dual) => portuguese ? (dual ? "Captura dupla do Kinovea" : "Captura do Kinovea") : (dual ? "Kinovea Dual Capture" : "Kinovea Capture");
    public string Start => portuguese ? "Iniciar" : "Start";
    public string Stop => portuguese ? "Parar" : "Stop";
    public string Stopped => portuguese ? "Parado" : "Stopped";
    public string WaitingForFrames => portuguese ? "aguardando quadros" : "waiting for frames";
    public string Frame(long sequence, int width, int height) => portuguese ? $"quadro {sequence} · {width}×{height}" : $"frame {sequence} · {width}×{height}";
    public string CaptureStopped(string reason) => portuguese ? $"Captura encerrada ({reason})." : $"Capture stopped ({reason}).";
}
