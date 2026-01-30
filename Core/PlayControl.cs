using Newtonsoft.Json;
using System.IO;
using System.Windows;
using Un4seen.Bass;

namespace MajdataEdit;

internal enum PlayMethod
{
    Normal,
    Op,
    Record
}

internal enum EditorControlMethod
{
    Start,
    Stop,
    OpStart,
    Pause,
    Continue,
    Record
}

public partial class MainWindow : Window
{
    // PLAYMETHOD CONTROL

    private async void Play(PlayMethod playMethod = PlayMethod.Normal)
    {
        //if (Op_Button.IsEnabled == false) return;  //?

        if (lastEditorState == EditorControlMethod.Start || playMethod != PlayMethod.Normal)
            if (!RequestStop())
                return;

        FumenContent.Focus();
        SaveFumen(false);
        if (CheckAndStartView()) return;
        Op_Button.IsEnabled = false;
        isPlaying = true;
        isPlan2Stop = false;
        PlayAndPauseButton.Content = "  ▌▌ ";

        await SimaiProcess.Serialize(GetRawFumenText());

        //TODO: Moeying改一下你的generateSoundEffect然后把下面这行删了
        var isOpIncluded = playMethod == PlayMethod.Normal ? false : true;

        var startAt = DateTime.Now;
        switch (playMethod)
        {
            case PlayMethod.Record:
                Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_FREQ, originFreq * GetPlaybackSpeed());
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                startAt = DateTime.Now.AddSeconds(5d);
                MessageBox.Show(GetLocalizedString("AskRender"), GetLocalizedString("Attention"));
                InternalSwitchWindow(false);
                generateSoundEffectList(0.0, isOpIncluded);
                var task = new Task(() => renderSoundEffect(5d));
                try
                {
                    task.Start();
                    task.Wait();
                }
                catch (AggregateException)
                {
                    MessageBox.Show(task.Exception!.InnerException!.Message + "\n" +
                                    task.Exception.InnerException.StackTrace);
                    return;
                }

                if (!RequestPlay(startAt, playMethod)) return;
                break;
            case PlayMethod.Op:
                generateSoundEffectList(0.0, isOpIncluded);
                InternalSwitchWindow(false);
                Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_FREQ, originFreq * GetPlaybackSpeed());
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                startAt = DateTime.Now.AddSeconds(5d);
                Bass.BASS_ChannelPlay(trackStartStream, true);
                await Task.Run(() =>
                {
                    if (!RequestPlay(startAt, playMethod)) return;
                    while (DateTime.Now.Ticks < startAt.Ticks)
                        if (lastEditorState != EditorControlMethod.Start)
                            return;
                    Dispatcher.Invoke(() =>
                    {
                        playStartTime =
                            Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
                        StartSELoop();
                        waveStopMonitorTimer.Start();
                        visualEffectRefreshTimer.Start();
                        Bass.BASS_ChannelPlay(bgmStream, false);
                    });
                });
                break;
            case PlayMethod.Normal:
                playStartTime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
                generateSoundEffectList(playStartTime, isOpIncluded);
                StartSELoop();
                waveStopMonitorTimer.Start();
                visualEffectRefreshTimer.Start();
                startAt = DateTime.Now;

                Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_FREQ, originFreq * GetPlaybackSpeed());
                Bass.BASS_ChannelPlay(bgmStream, false);
                await Task.Run(() =>
                {
                    if (lastEditorState == EditorControlMethod.Pause)
                    {
                        if (!RequestContinue(startAt)) return;
                    }
                    else
                    {
                        if (!RequestPlay(startAt, playMethod)) return;
                    }
                });
                break;
        }

        CursorTime = (float)(SimaiProcess.timingLists[selectedDifficulty].Find(n =>
        {
            return n.RawTextPosition <= GetRawFumenPosition() &&
                   n.RawTextPosition + n.RawContent.Length >= GetRawFumenPosition();
        })?.Timing ?? 0d);
        draw_wave();
    }

    private void Pause()
    {
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;

        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        RequestPause();
        draw_wave();
    }

    private void Stop()
    {
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;

        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        RequestStop();
        Bass.BASS_ChannelSetPosition(bgmStream, playStartTime);
        draw_wave();
    }

    private void TogglePlayAndPause(PlayMethod playMethod = PlayMethod.Normal)
    {
        if (isPlaying)
            Pause();
        else
            Play(playMethod);
    }

    private void TogglePlayAndStop(PlayMethod playMethod = PlayMethod.Normal)
    {
        if (isPlaying)
            Stop();
        else
            Play(playMethod);
    }


    // PLAYBACK SPEED CONTROL

    private void SetPlaybackSpeed(int speedItem)
    {
        speedItem = Math.Max(0, speedItem);
        speedItem = Math.Min(PlayBackSpeedSelector.Items.Count - 1, speedItem);
        PlayBackSpeedSelector.SelectedIndex = speedItem;
    }

    private void SetPlaybackSpeedDiff(int speedItemDiff)
    {
        SetPlaybackSpeed(PlayBackSpeedSelector.SelectedIndex + speedItemDiff);
    }

    private float GetPlaybackSpeed()
    {
        int speed = 0;
        this.Dispatcher.Invoke(() =>
        {
            speed = PlayBackSpeedSelector.SelectedIndex switch
            {
                0 => -90,
                1 => -75,
                2 => -50,
                3 => -25,
                4 => 0,
                5 => 50,
                6 => 75,
                7 => 100,
                _ => 0
            };
        });

        return speed / 100f + 1f;
    }

    private void SetBgmPosition(double time)
    {
        if (lastEditorState == EditorControlMethod.Pause) RequestStop();
        Bass.BASS_ChannelSetPosition(bgmStream, time);
    }


    // VIEW COMMUNICATION
    private bool RequestStop()
    {
        var requestStop = new EditRequestjson
        {
            control = EditorControlMethod.Stop
        };
        var json = JsonConvert.SerializeObject(requestStop);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Stop;
        return true;
    }

    private bool RequestPause()
    {
        var requestStop = new EditRequestjson
        {
            control = EditorControlMethod.Pause
        };
        var json = JsonConvert.SerializeObject(requestStop);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Pause;
        return true;
    }

    private bool RequestContinue(DateTime StartAt)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Continue,
            startAt = StartAt.Ticks,
            startTime = (float)Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream)),
            audioSpeed = GetPlaybackSpeed(),
            editorPlayMethod = editorSetting!.editorPlayMethod
        };
        var json = JsonConvert.SerializeObject(request);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Start;
        return true;
    }

    private bool RequestPlay(DateTime StartAt, PlayMethod playMethod)
    {
        var jsonStruct = new Majson();
        foreach (var note in SimaiProcess.noteLists[selectedDifficulty])
        {
            jsonStruct.timingList.Add(note);
        }

        jsonStruct.title = SimaiProcess.simaiFile.Title;
        jsonStruct.artist = SimaiProcess.simaiFile.Artist;
        jsonStruct.level = SimaiProcess.levels[selectedDifficulty];
        jsonStruct.designer = SimaiProcess.designers[selectedDifficulty];
        jsonStruct.difficulty = SimaiProcess.GetDifficultyText(selectedDifficulty);
        jsonStruct.diffNum = selectedDifficulty;
        jsonStruct.wholebpm = SimaiProcess.simaiFile.Commands.FirstOrDefault(n => n.Prefix == "wholebpm").Value ?? "0";
        jsonStruct.mode = IsDeluxeChart() ? ChartMode.Deluxe : ChartMode.Standard;

        var json = JsonConvert.SerializeObject(jsonStruct);
        var path = maidataDir + "/majdata.json";
        File.WriteAllText(path, json);

        var request = new EditRequestjson();
        if (playMethod == PlayMethod.Op)
            request.control = EditorControlMethod.OpStart;
        else if (playMethod == PlayMethod.Normal)
            request.control = EditorControlMethod.Start;
        else
            request.control = EditorControlMethod.Record;

        Dispatcher.Invoke(() =>
        {
            request.jsonPath = path;
            request.startAt = StartAt.Ticks;
            request.startTime =
                (float)Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
            // request.playSpeed = float.Parse(ViewerSpeed.Text);
            // 将maimaiDX速度换算为View中的单位速度 MajSpeed = 107.25 / (71.4184491 * (MaiSpeed + 0.9975) ^ -0.985558604)
            request.noteSpeed = editorSetting!.playSpeed;
            request.touchSpeed = editorSetting!.touchSpeed;
            request.backgroundCover = editorSetting!.backgroundCover;
            request.comboStatusType = editorSetting!.comboStatusType;
            request.audioSpeed = GetPlaybackSpeed();
            request.smoothSlideAnime = editorSetting!.SmoothSlideAnime;
            request.editorPlayMethod = editorSetting.editorPlayMethod;
        });

        json = JsonConvert.SerializeObject(request);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Start;
        return true;
    }
}
