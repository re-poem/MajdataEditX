using DiffMatchPatch;
using DiscordRPC;
using MajdataEdit.ChartShare;
using MajSimai;
using Microsoft.AspNetCore.SignalR.Client;
using Semver;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using WPFLocalizeExtension.Extensions;
using Timer = System.Timers.Timer;

namespace MajdataEdit;

public partial class MainWindow : Window
{
    //// Common
    public static readonly string MAJDATA_VERSION_STRING = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    public static readonly SemVersion MAJDATA_VERSION = SemVersion.Parse(MAJDATA_VERSION_STRING, SemVersionStyles.Any);

    //// File
    public static string maidataDir = "";
    public static string audioDir = "";
    public static bool useOgg = false;
    public static string originMaidataDir = ""; //ShareMode && Host Only

    private const string majSettingFilename = "majSetting.json";
    private const string editorSettingFilename = "EditorSetting.json";

    private readonly short[][] waveRaws = new short[3][];

    //// Editor
    public DiscordRpcClient discordRpcClient = new("1068882546932326481");

    public EditorSetting? editorSetting;
    private bool UpdateCheckLock;

    // Fumen
    public static int selectedDifficulty = 0;

    public readonly Timer chartChangeTimer = new(1000); // 谱面变更延迟解析
    public readonly Timer currentTimeRefreshTimer = new(100);
    private bool fumenOverwriteMode; // 谱面文本覆盖模式
    public bool isSaved = true;

    private int findPosition;
    private int lastFindPosition;

    // Playing
    private float CursorTime;
    private EditorControlMethod lastEditorState;

    // Wave
    private double lastMousePointX; //Used for drag scroll
    private double songLength;

    // Audio
    private SoundSetting soundSetting = new();
    public float originFreq = 44100f;

    // UI Draw
    private readonly Timer visualEffectRefreshTimer = new(1);
    private WriteableBitmap? WaveBitmap;

    // Error Handle
    private static ErrorList errorListWindow = new();
    private static Error? fatalError;

    // Chart Share
    private HubConnection? _client;    // 客户端对象
    private bool _isRemoteUpdate = false; // 防死循环的锁
    private string _shadowText = ""; // 谱面文本同步缓冲
    private diff_match_patch _dmp = new();
    private readonly byte[] certBytes = new byte[] {
    48, 130, 2, 10, 2, 130, 2, 1, 0, 162, 108, 70, 147, 186, 10, 181,
    148, 40, 72, 232, 165, 67, 36, 174, 170, 138, 116, 59, 92, 233, 84, 241,
    49, 216, 212, 68, 156, 171, 157, 12, 59, 223, 168, 166, 169, 201, 231, 90,
    168, 64, 77, 121, 36, 195, 139, 44, 219, 231, 132, 213, 156, 181, 59, 109,
    251, 129, 55, 125, 135, 239, 156, 91, 62, 3, 9, 48, 201, 202, 138, 148,
    189, 125, 111, 121, 243, 8, 109, 45, 48, 192, 151, 25, 170, 181, 104, 215,
    26, 23, 136, 228, 162, 11, 122, 94, 27, 172, 168, 46, 159, 23, 183, 115,
    172, 222, 80, 196, 48, 54, 206, 188, 202, 133, 149, 196, 92, 203, 204, 188,
    117, 221, 23, 229, 162, 179, 96, 194, 249, 244, 85, 108, 97, 12, 150, 13,
    6, 9, 212, 110, 114, 78, 139, 23, 172, 93, 238, 197, 24, 151, 165, 190,
    52, 100, 168, 36, 174, 205, 96, 224, 251, 30, 254, 237, 112, 71, 191, 118,
    159, 159, 4, 98, 35, 229, 39, 252, 184, 15, 225, 252, 4, 148, 117, 125,
    128, 3, 65, 116, 39, 253, 248, 137, 45, 52, 247, 162, 30, 213, 144, 157,
    108, 32, 78, 28, 195, 143, 241, 223, 215, 97, 40, 80, 117, 75, 187, 108,
    108, 80, 116, 90, 199, 215, 201, 153, 10, 83, 0, 166, 40, 15, 12, 98,
    140, 162, 23, 33, 83, 81, 40, 15, 69, 144, 106, 201, 118, 202, 9, 227,
    242, 16, 128, 196, 123, 81, 187, 86, 43, 206, 138, 1, 230, 112, 6, 54,
    237, 147, 5, 192, 42, 30, 162, 189, 125, 227, 74, 81, 37, 47, 12, 108,
    182, 237, 42, 95, 11, 211, 252, 43, 124, 95, 75, 35, 27, 49, 209, 219,
    165, 191, 217, 99, 195, 97, 77, 102, 243, 5, 100, 39, 249, 65, 71, 181,
    162, 27, 208, 112, 84, 41, 243, 232, 164, 153, 211, 167, 121, 93, 172, 210,
    9, 132, 47, 235, 114, 216, 116, 103, 117, 232, 97, 60, 132, 12, 11, 83,
    191, 220, 174, 6, 106, 158, 107, 43, 143, 206, 125, 75, 161, 26, 254, 249,
    149, 94, 194, 54, 98, 230, 73, 194, 15, 160, 186, 52, 118, 224, 9, 137,
    43, 212, 252, 47, 171, 160, 156, 90, 96, 222, 79, 234, 211, 10, 115, 55,
    249, 124, 187, 4, 133, 170, 184, 58, 31, 50, 179, 156, 40, 156, 65, 210,
    203, 176, 152, 41, 67, 10, 164, 79, 194, 12, 133, 9, 199, 82, 21, 86,
    239, 42, 46, 244, 14, 82, 8, 125, 222, 31, 208, 229, 102, 147, 194, 29,
    22, 111, 79, 28, 90, 218, 150, 24, 154, 38, 17, 160, 29, 180, 74, 57,
    203, 161, 86, 240, 84, 41, 63, 106, 8, 163, 23, 55, 143, 65, 192, 4,
    150, 161, 20, 51, 103, 55, 162, 250, 184, 38, 134, 4, 253, 50, 223, 101,
    90, 153, 38, 20, 40, 110, 201, 51, 178, 34, 39, 188, 47, 210, 250, 31,
    212, 43, 205, 157, 251, 63, 121, 199, 5, 2, 3, 1, 0, 1};
    private Dictionary<string, RemoteCursor> _cursors = new();

    // window state
    private bool IsLoading = false;
    private bool IsShare = false;
    private bool IsHost = false;


    ///////// Common Helpers /////////
    public static string GetLocalizedString(string key, string resourceFileName = "Langs")
    {
        // Build up the fully-qualified name of the key

        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var fullKey = assemblyName + ":" + resourceFileName + ":" + key;
        var locExtension = new LocExtension(fullKey);
        locExtension.ResolveLocalizedValue(out string? localizedString);

        return localizedString ?? key;
    }

    // 获取本机局域网IP
    public static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }

    public bool IsDeluxeChart()
    {
        foreach (var noteGroup in SimaiProcess.noteLists[selectedDifficulty])
            foreach (var note in noteGroup.Notes)
                if (note.IsEx) // tap
                    return true;
                else if (note.Type == SimaiNoteType.Hold && note.IsBreak) // break hold
                    return true;
                else if (note.Type is SimaiNoteType.Touch or SimaiNoteType.TouchHold) // touch
                    return true;
                else if (note.Type == SimaiNoteType.Slide) // slide
                    if (note.IsSlideBreak) //break slide
                        return true;
                    else                   //festival slide
                        foreach (var c in new[] { "-", "^", "v", "<", ">", "V", "s", "z", "w", "qq", "pp" })
                            if (((note.RawContent!.Length - note.RawContent!.Replace(c, "").Length) / c.Length) > 1)
                                return true;

        return false;
    }
}
