using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TopRacerLauncher;

/// <summary>
/// Поле ввода пароля со звёздочками. Снаружи (в Text) всегда только маска,
/// настоящий пароль доступен через <see cref="Secret"/>.
/// </summary>
public sealed class MaskedInput : TextBox
{
    private const char MaskChar = '*';
    private bool _rendering;

    public MaskedInput()
    {
        IsUndoEnabled = false;
        AcceptsReturn = false;
        AcceptsTab = false;
        DataObject.AddPastingHandler(this, OnPaste);
    }

    /// <summary>Введённый пароль без маски.</summary>
    public string Secret { get; private set; } = "";

    /// <summary>Очищает и маску, и сам пароль.</summary>
    public new void Clear()
    {
        Secret = "";
        Render();
        base.Clear();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        e.Handled = true;

        var text = e.Text;
        if (string.IsNullOrEmpty(text)) return;

        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0) return;

        if (MaxLength > 0)
        {
            int room = MaxLength - Secret.Length;
            if (room <= 0) return;
            if (text.Length > room) text = text[..room];
        }

        Secret += text;
        Render();
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        switch (e.Key)
        {
            case Key.Back:
            case Key.Delete:
                e.Handled = true;
                if (Secret.Length > 0)
                {
                    Secret = Secret[..^1];
                    Render();
                }
                break;

            case Key.Left:
            case Key.Right:
            case Key.Home:
            case Key.End:
            case Key.Up:
            case Key.Down:
                e.Handled = true;   // каретка всегда в конце
                break;

            case Key.A:
            case Key.C:
            case Key.X:
                if (ctrl) e.Handled = true;   // копировать/резать маску незачем
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);

        // Текст правили в обход ввода (drag&drop, автозамена) — возвращаем маску.
        if (!_rendering && Text.Length != Secret.Length) Render();
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();

        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)) return;

        var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
        if (string.IsNullOrEmpty(text)) return;

        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0) return;

        if (MaxLength > 0)
        {
            int room = MaxLength - Secret.Length;
            if (room <= 0) return;
            if (text.Length > room) text = text[..room];
        }

        Secret += text;
        Render();
    }

    private void Render()
    {
        _rendering = true;
        Text = new string(MaskChar, Secret.Length);
        CaretIndex = Text.Length;
        SelectionLength = 0;
        _rendering = false;
    }
}
