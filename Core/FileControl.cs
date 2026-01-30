using MajdataEdit.AutoSaveModule;
using MajdataEdit.ChartShare;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Fx;

namespace MajdataEdit;

// 文件控制部分

public partial class MainWindow : Window
{
    /// <summary>
    /// 清理所有变量（或及窗口）
    /// 不负责检查文件是否保存，请在任何调用前适当加上 if (!isSaved) if (!AskSaveFumen()) return;
    /// </summary>
    /// <param name="setEmpty">是否清除窗口元素</param>
    public async void ClearWindow(bool setEmpty = false)
    {
        Stop();

        // share
        if (IsShare) await ToggleChartShare();

        SaveSetting();

        // clear data
        soundSetting?.Close();
        audioDir = "";
        maidataDir = "";
        FumenContent.Clear();
        SimaiProcess.Clear();
        LevelSelector.SelectedItem = "";
        OffsetTextBox.Text = "";

        // about save
        AutoSaveManager.Of().SetAutoSaveEnable(false);
        SetSavedState(true);

        if (setEmpty) set_empty();
    }
    public async Task InitFromFile(string path) //file name should not be included in path
    {
        set_loading(true);

        // close all
        ClearWindow();

        // initalize data
        if (editorSetting == null) ReadEditorSetting();

        // check files
        useOgg = File.Exists(path + "/track.ogg");
        var audioPath = path + "/track" + (useOgg ? ".ogg" : ".mp3");
        audioDir = audioPath;
        var dataPath = path + "/maidata.txt";
        if (!File.Exists(audioPath))
        {
            MessageBox.Show(GetLocalizedString("NoTrack"), GetLocalizedString("Error"));
            return;
        }
        if (!File.Exists(dataPath))
        {
            MessageBox.Show(GetLocalizedString("NoMaidata_txt"), GetLocalizedString("Error"));
            return;
        }
        maidataDir = path;

        // about save
        SafeTerminationDetector.Of().ChangePath(maidataDir);

        // music initalize
        var decodeStream = Bass.BASS_StreamCreateFile(audioPath, 0L, 0L, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
        bgmStream = BassFx.BASS_FX_TempoCreate(decodeStream, BASSFlag.BASS_FX_FREESOURCE);
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_FREQ, ref originFreq);

        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(allperfectStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(fanfareStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(clockStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Answer_Level);
        Bass.BASS_ChannelSetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Judge_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakSlideStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStartStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Break_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Ex_Level);
        Bass.BASS_ChannelSetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Touch_Level);
        Bass.BASS_ChannelSetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Hanabi_Level);
        Bass.BASS_ChannelSetAttribute(holdRiserStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Hanabi_Level);
        var info = Bass.BASS_ChannelGetInfo(bgmStream);
        if (info.freq != 44100) MessageBox.Show(GetLocalizedString("Warn44100Hz"), GetLocalizedString("Attention"));
        
        // decode wave
        ReadWaveFromFile();

        // load data
        await SimaiProcess.ReadAll(dataPath);
        LevelSelector.SelectedItem = LevelSelector.Items[0];
        ReadSetting();
        SetRawFumenText(SimaiProcess.fumens[selectedDifficulty]);
        await SimaiProcess.Serialize(GetRawFumenText());
        SeekTextFromTime();
        OffsetTextBox.Text = SimaiProcess.simaiFile.Offset.ToString();

        AutoSaveManager.Of().SetAutoSaveEnable(true);
        SetSavedState(true);
        SyntaxCheck();

        set_loading(false);
    }

    public async Task InitFromShare(string fileUrl, GuestInitDto data)
    {
        set_loading(true);

        // close all
        ClearWindow();

        // initalize data
        if (editorSetting == null) ReadEditorSetting();

        // check path
        var basePath = Environment.CurrentDirectory + "/Sharing";
        Directory.CreateDirectory(basePath); //防止没有Sharing文件夹

        // get files
        useOgg = data.UseOgg;
        HttpClient httpClient = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                if (cert == null) return false;
                return cert.GetPublicKey().SequenceEqual(certBytes);
            }
        });
        //下载音频
        var trackName = "track" + (useOgg ? ".ogg" : ".mp3");
        string localAudioPath = Path.Combine(basePath, trackName);
        using (var stream = await httpClient.GetStreamAsync(fileUrl + "/" + trackName))
        using (var fs = new FileStream(localAudioPath, FileMode.Create))
        {
            await stream.CopyToAsync(fs);
        }
        //下载MajSettings
        string localSettingPath = Path.Combine(basePath, majSettingFilename);
        byte[] settingBytes = await httpClient.GetByteArrayAsync(fileUrl + "/" + majSettingFilename);
        await File.WriteAllBytesAsync(localSettingPath, settingBytes);

        if (IsHost) originMaidataDir = maidataDir;
        maidataDir = basePath;
        audioDir = localAudioPath;

        // music initalize
        var decodeStream = Bass.BASS_StreamCreateFile(audioDir, 0L, 0L, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
        bgmStream = BassFx.BASS_FX_TempoCreate(decodeStream, BASSFlag.BASS_FX_FREESOURCE);
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_FREQ, ref originFreq);

        //ReadSetting(); //此处不用该函数并自定义覆盖，没有default打底但绝逼有拉过来的数据所以没关系
        var setting = JsonConvert.DeserializeObject<MajSetting>(File.ReadAllText(localSettingPath));
        SetBgmPosition(setting!.lastEditTime);
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(allperfectStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(fanfareStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(clockStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Answer_Level);
        Bass.BASS_ChannelSetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Judge_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Ex_Level);
        Bass.BASS_ChannelSetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Touch_Level);
        Bass.BASS_ChannelSetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);
        Bass.BASS_ChannelSetAttribute(holdRiserStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);

        var info = Bass.BASS_ChannelGetInfo(bgmStream);
        if (info.freq != 44100) MessageBox.Show(GetLocalizedString("Warn44100Hz"), GetLocalizedString("Attention"));

        // decode wave
        ReadWaveFromFile();


        // load data
        //if (!SimaiProcess.ReadData(dataPath)) return;
        SimaiProcess.simaiFile.Title = data.Name;
        SimaiProcess.simaiFile.Offset = data.Offset;
        selectedDifficulty = data.Diff;
        SimaiProcess.levels[selectedDifficulty] = data.Level;
        SimaiProcess.fumens[selectedDifficulty] = data.Text;
        LevelSelector.SelectedIndex = selectedDifficulty;
        OffsetTextBox.Text = SimaiProcess.simaiFile.Offset.ToString();

        SetRawFumenText(SimaiProcess.fumens[selectedDifficulty]);
        await SimaiProcess.Serialize(GetRawFumenText());
        SeekTextFromTime();

        AutoSaveManager.Of().SetAutoSaveEnable(false);
        isSaved = true;
        SyntaxCheck();
        _shadowText = FumenContent.Text; // 影子文本和UI直接挂钩，没必要用不带\r的

        set_loading(false);
    }

    public void SetSavedState(bool state) // UI修改和程序逻辑被迫混在一起了
    {
        if (IsShare)
        {
            if (state)
            {
                isSaved = true;
                TheWindow.Title = GetWindowsTitleString(SimaiProcess.simaiFile.Title + " Share");
            }
            else
            {
                isSaved = false;
                TheWindow.Title = GetWindowsTitleString(GetLocalizedString("Unsaved") + SimaiProcess.simaiFile.Title! + " Share");
            }
        }
        else
        {
            if (state)
            {
                isSaved = true;
                LevelSelector.IsEnabled = true;
                TheWindow.Title = GetWindowsTitleString(SimaiProcess.simaiFile.Title);
            }
            else
            {
                isSaved = false;
                LevelSelector.IsEnabled = false;
                TheWindow.Title = GetWindowsTitleString(GetLocalizedString("Unsaved") + SimaiProcess.simaiFile.Title);
                AutoSaveManager.Of().SetFileChanged();
            }
        }
    }

    private void CreateNewFumen(string path)
    {
        if (File.Exists(path + "/maidata.txt"))
            MessageBox.Show(GetLocalizedString("MaidataExist"));
        else
            File.WriteAllText(path + "/maidata.txt",
                "&title=" + GetLocalizedString("SetTitle") + "\n" +
                "&artist=" + GetLocalizedString("SetArtist") + "\n" +
                "&des=" + GetLocalizedString("SetDes") + "\n" +
                "&first=0\n");
    }

    /// <summary>
    ///     Ask the user and save fumen.
    /// </summary>
    /// <returns>Return false if user cancel the action</returns>
    public bool AskSaveFumen(bool canCancel = true)
    {
        var result = MessageBox.Show(GetLocalizedString("AskSave"), GetLocalizedString("Warning"),
            canCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo);
        if (result == MessageBoxResult.Yes)
        {
            SaveFumen(true);
            return true;
        }

        if (result == MessageBoxResult.Cancel) return false;
        return true;
    }
    private void SaveFumen(bool writeToDisk)
    {
        if (string.IsNullOrWhiteSpace(maidataDir)) return;

        string _maidataDir = maidataDir;
        if (IsShare)
        {
            if (IsHost) _maidataDir = originMaidataDir;
            else
            {
                _client!.InvokeAsync(nameof(ChartHub.SaveFumen), false);
                return;
            }
            _client!.InvokeAsync(nameof(ChartHub.SaveFumen), true);
        }

        SimaiProcess.fumens[selectedDifficulty] = GetRawFumenText();
        SimaiProcess.simaiFile.Offset = float.Parse(OffsetTextBox.Text);

        SyntaxCheck();

        SimaiProcess.SaveData(_maidataDir + "/maidata.bak.txt");
        SaveSetting();
        if (writeToDisk)
        {
            SimaiProcess.SaveData(_maidataDir + "/maidata.txt");
            SetSavedState(true);
        }
    }




    //////////////////// Helper Functions ////////////////////

    private void ReadWaveFromFile()
    {
        var bgmDecode = Bass.BASS_StreamCreateFile(audioDir, 0L, 0L, BASSFlag.BASS_STREAM_DECODE);
        try
        {
            songLength = Bass.BASS_ChannelBytes2Seconds(bgmDecode,
                Bass.BASS_ChannelGetLength(bgmDecode, BASSMode.BASS_POS_BYTE));
            Bass.BASS_StreamFree(bgmDecode);
            var bgmSample = Bass.BASS_SampleLoad(audioDir, 0, 0, 1, BASSFlag.BASS_DEFAULT);
            var bgmInfo = Bass.BASS_SampleGetInfo(bgmSample);
            var freq = bgmInfo.freq;
            var sampleCount = (long)(songLength * freq * 2);
            var bgmRAW = new short[sampleCount];
            Bass.BASS_SampleGetData(bgmSample, bgmRAW);

            waveRaws[0] = new short[sampleCount / 20 + 1];
            for (var i = 0; i < sampleCount; i = i + 20) waveRaws[0][i / 20] = bgmRAW[i];
            waveRaws[1] = new short[sampleCount / 50 + 1];
            for (var i = 0; i < sampleCount; i = i + 50) waveRaws[1][i / 50] = bgmRAW[i];
            waveRaws[2] = new short[sampleCount / 100 + 1];
            for (var i = 0; i < sampleCount; i = i + 100) waveRaws[2][i / 100] = bgmRAW[i];
        }
        catch (Exception e)
        {
            MessageBox.Show("mp3/ogg解码失败。\nMP3/OGG Decode fail.\n" + e.Message + Bass.BASS_ErrorGetCode());
            Bass.BASS_StreamFree(bgmDecode);
            Process.Start("https://github.com/LingFeng-bbben/MajdataEdit/issues/26");
        }
    }

    private void SaveSetting()
    {
        if (maidataDir == "") return;
        var setting = new MajSetting
        {
            lastEditDiff = selectedDifficulty,
            lastEditTime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream))
        };
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.BGM_Level);
        Bass.BASS_ChannelGetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Answer_Level);
        Bass.BASS_ChannelGetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Judge_Level);
        Bass.BASS_ChannelGetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Break_Level);
        Bass.BASS_ChannelGetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Break_Slide_Level);
        Bass.BASS_ChannelGetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Ex_Level);
        Bass.BASS_ChannelGetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Touch_Level);
        Bass.BASS_ChannelGetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Slide_Level);
        Bass.BASS_ChannelGetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Hanabi_Level);
        var json = JsonConvert.SerializeObject(setting);
        File.WriteAllText(maidataDir + "/" + majSettingFilename, json);
    }

    private void ReadSetting()
    {
        var path = maidataDir + "/" + majSettingFilename;
        if (!File.Exists(path)) return;
        var setting = JsonConvert.DeserializeObject<MajSetting>(File.ReadAllText(path));
        LevelSelector.SelectedIndex = setting!.lastEditDiff;
        selectedDifficulty = setting.lastEditDiff;
        SetBgmPosition(setting.lastEditTime);
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(allperfectStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(fanfareStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(clockStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Answer_Level);
        Bass.BASS_ChannelSetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Judge_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Ex_Level);
        Bass.BASS_ChannelSetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Touch_Level);
        Bass.BASS_ChannelSetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);
        Bass.BASS_ChannelSetAttribute(holdRiserStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);

        SaveSetting(); // 覆盖旧setting
    }
}
