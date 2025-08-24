using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MajdataEdit;

/// <summary>
///     Window1.xaml 的交互逻辑
/// </summary>
[Obsolete("ErrorInfo is Obsolete,Use Error")]
public class ErrorInfo
{
    public int positionX;
    public int positionY;

    public ErrorInfo(int _posX, int _posY)
    {
        positionX = _posX;
        positionY = _posY;
    }
}

[Obsolete("MuriCheckResult is Obsolete,Use ErrorList(ErrorListWindow)")]
public partial class MuriCheckResult : Window
{
    public List<ErrorInfo> errorPosition = new();

    public MuriCheckResult()
    {
        InitializeComponent();
    }

    public void ListBoxItem_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = (ListBoxItem)sender;
        var index = int.Parse(item.Name[2..]);
        var errorInfo = errorPosition[index];

        ((MainWindow)Owner).ScrollToFumenContentSelection(errorInfo.positionX, errorInfo.positionY);
    }
}