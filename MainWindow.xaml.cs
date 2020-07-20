using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FVSelectScenes
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string c_defaultDirectory = @"\\akershus\archive\Video\Family\From8mmTape";
        const string c_csvSuffix = " scenes.csv";

        static readonly TimeSpan s_timerInterval = TimeSpan.FromMilliseconds(50);
        static readonly TimeSpan s_sampleLength = TimeSpan.FromSeconds(1);

        string m_videofilename;
        string m_csvfilename;
        List<SegmentModel> m_segments;

        TimeSpan m_pauseAt = TimeSpan.MaxValue;
        int m_currentSegment;
        bool m_playerIsPlaying = false;
        int m_playingSegment;
        TimeSpan m_endOfRecording = TimeSpan.FromHours(3);
        bool m_finishClose = false;

        int m_firstSegmentInScene;
        int m_lastSegmentInScene;

        TimeSpan m_pausePosition;

        public MainWindow()
        {
            InitializeComponent();
            BeginInit();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = s_timerInterval;
            timer.Tick += Timer_Tick;
            timer.Start();
            EndInit();

            Dispatcher.BeginInvoke((Action)OpenFile, DispatcherPriority.ApplicationIdle);
        }

        private void OpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".mp4";
            dlg.Filter = "Video files (.mp4)|*.mp4";
            if (System.IO.Directory.Exists(c_defaultDirectory))
            {
                dlg.InitialDirectory = c_defaultDirectory;
            }
            var result = dlg.ShowDialog();
            if (result == true)
            {
                VideoFilename = dlg.FileName;
            }
            else
            {
                m_finishClose = true;
                Close();
            }
        }

        public string VideoFilename
        {
            set
            {
                m_videofilename = value;
                m_csvfilename = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(value),
                    System.IO.Path.GetFileNameWithoutExtension(value) + c_csvSuffix);
                x_filename.Content = System.IO.Path.GetFileNameWithoutExtension(value);

                if (System.IO.File.Exists(m_videofilename) && System.IO.File.Exists(m_csvfilename))
                {
                    LoadFiles();
                }
            }
        }

        private void LoadFiles()
        {
            x_player.Source = new Uri(m_videofilename);
            m_segments = SegmentModel.LoadFromCsv(m_csvfilename);
            m_currentSegment = 0;
            LoadSegmentInfo();
            PlayMomentarily();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (m_finishClose)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Dispatcher.BeginInvoke((Action)PromptForSave);
        }

        private void PromptForSave()
        {
            var result = MessageBox.Show("Save segments before exit?", "Closing", MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                SaveSegmentInfo();
                SegmentModel.SaveToCsv(m_segments, m_csvfilename);
            }

            m_finishClose = true;
            Close();
        }

        private void LoadSegmentInfo()
        {
            var segment = m_segments[m_currentSegment];

            x_segmentNumber.Text = (m_currentSegment+1).ToString();
            x_segmentTotal.Content = m_segments.Count.ToString();

            var startPosition = m_segments[m_currentSegment].Position;
            var endPosition = GetSegmentEnd(m_currentSegment);

            x_start.Content = FormatTimespan(startPosition);
            x_end.Content = FormatTimespan(endPosition);
            x_duration.Content = FormatTimespan(endPosition.Subtract(startPosition));

            switch(segment.Disposition)
            {
                case SegmentDisposition.Delete:
                    x_dispDelete.IsChecked = true;
                    break;

                case SegmentDisposition.Keep:
                    x_dispKeep.IsChecked = true;
                    break;

                case SegmentDisposition.AddToPrevious:
                    x_dispAdd.IsChecked = true;
                    break;
            }

            // If previous segment is deleted, disable add to previous
            if (m_currentSegment <= 0
                || m_segments[m_currentSegment-1].Disposition == SegmentDisposition.Delete)
            {
                // If add to previous is set, change to keep
                if (x_dispAdd.IsChecked == true)
                {
                    x_dispKeep.IsChecked = true;
                }
                x_dispAdd.IsEnabled = false;
            }
            else
            {
                x_dispAdd.IsEnabled = true;
            }

            if (segment.Date != DateTime.MinValue)
            {
                x_date.SelectedDate = segment.Date;
            }
            else
            {
                x_date.SelectedDate = null;
            }

            x_subject.Text = segment.Subject;
            x_title.Text = segment.Title;

            x_player.Position = segment.Position;

            UpdatePositionDisplay();
            UpdateSceneDisplay();
        }

        private void UpdateSceneDisplay()
        {
            // Find the first segment in the scene
            m_firstSegmentInScene = m_currentSegment;
            if (x_dispAdd.IsChecked == true)
            {
                while (m_firstSegmentInScene > 0)
                {
                    --m_firstSegmentInScene;
                    if (m_segments[m_firstSegmentInScene].Disposition != SegmentDisposition.AddToPrevious)
                        break;
                }
            }

            // Find the last segment in the scene
            m_lastSegmentInScene = m_currentSegment;
            if (x_dispDelete.IsChecked != true)
            {
                while (m_lastSegmentInScene < m_segments.Count - 1 &&
                    m_segments[m_lastSegmentInScene + 1].Disposition == SegmentDisposition.AddToPrevious)
                {
                    ++m_lastSegmentInScene;
                }
            }

            var sceneStart = m_segments[m_firstSegmentInScene].Position;
            var sceneEnd = GetSegmentEnd(m_lastSegmentInScene);

            x_sceneStart.Content = FormatTimespan(sceneStart);
            x_sceneEnd.Content = FormatTimespan(sceneEnd);
            x_sceneDuration.Content = FormatTimespan(sceneEnd.Subtract(sceneStart));

            // Update the display
            x_sceneFirstSegment.Content = (m_firstSegmentInScene + 1).ToString();
            x_sceneLastSegment.Content = (m_lastSegmentInScene + 1).ToString();
        }

        private void SaveSegmentInfo()
        {
            var segment = m_segments[m_currentSegment];

            if (x_dispDelete.IsChecked == true)
            {
                segment.Disposition = SegmentDisposition.Delete;
            }
            else if (x_dispKeep.IsChecked == true)
            {
                segment.Disposition = SegmentDisposition.Keep;
            }
            else if (x_dispAdd.IsChecked == true)
            {
                segment.Disposition = SegmentDisposition.AddToPrevious;
            }

            if (x_date.SelectedDate.HasValue)
            {
                segment.Date = x_date.SelectedDate.Value;
            }
            else
            {
                segment.Date = DateTime.MinValue;
            }

            segment.Subject = x_subject.Text;
            segment.Title = x_title.Text;
        }

        // A more friendly timespan formatter
        private string FormatTimespan(TimeSpan ts)
        {
            var result = ts.ToString(@"hh\:mm\:ss\.fff");
            if (result.StartsWith("00:"))
            {
                result = result.Substring(3);
                if (result.StartsWith("00:"))
                    result = result.Substring(3);
            }
            return result;
        }

        // A more forgiving TimeSpan parser
        private bool TryParseTimespan(string value, out TimeSpan ts)
        {
            decimal seconds = 0;
            foreach(var part in value.Split(':'))
            {
                seconds *= 60;
                decimal parsed;
                if (!decimal.TryParse(part, out parsed))
                {
                    ts = default;
                    return false;
                }
                seconds += parsed;
            }

            ts = new TimeSpan((long)(seconds * 10000000));
            return true;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (m_playerIsPlaying)
            {
                UpdatePositionDisplay(true);
                if (x_player.Position > m_pauseAt)
                    Pause();
            }
        }

        private void SeekAndSample(TimeSpan centerPosition, TimeSpan length = default)
        {
            if (length == default)
            {
                length = s_sampleLength;
            }
            long startTicks = centerPosition.Ticks - length.Ticks / 2;
            long endTicks = centerPosition.Ticks + length.Ticks / 2;
            if (startTicks < 0) startTicks = 0;
            if (endTicks > m_endOfRecording.Ticks) endTicks = m_endOfRecording.Ticks;
            Play(new TimeSpan(startTicks), new TimeSpan(endTicks));
        }

        private void UpdatePositionDisplay(bool beepOnSegmentChange = false)
        {
            var position = x_player.Position;
            x_position.Text = FormatTimespan(position);

            if (position < m_segments[m_playingSegment].Position
                || (m_playingSegment < m_segments.Count-1 && position > m_segments[m_playingSegment+1].Position))
            {
                System.Diagnostics.Debug.WriteLine("next segment");
                // Set the playing segment
                m_playingSegment = m_segments.Count - 1;
                for (int i = 0; i < m_segments.Count; ++i)
                {
                    if (m_segments[i].Position > position)
                    {
                        m_playingSegment = i - 1;
                        break;
                    }
                }

                x_playingSegment.Content = (m_playingSegment+1).ToString();
                if (beepOnSegmentChange)
                {
                    System.Console.Beep(1046, 250);
                }
            }
        }

        private void x_play_Click(object sender, RoutedEventArgs e)
        {
            Play();
        }

        private void Pause_Event(object sender, RoutedEventArgs e)
        {
            Pause();
        }

        private void Pause()
        {
            m_playerIsPlaying = false;
            x_player.Pause();
            UpdatePositionDisplay();
        }

        private void Play()
        {
            m_pauseAt = TimeSpan.MaxValue;
            m_playerIsPlaying = true;
            x_player.Play();
        }

        private void Play(TimeSpan start, TimeSpan end)
        {
            m_pauseAt = new TimeSpan(end.Ticks - (s_timerInterval.Ticks * 3) / 2);
            x_player.Position = start;
            UpdatePositionDisplay();
            m_playerIsPlaying = true;
            x_player.Play();
        }

        private void TextBox_UpdateOnEnterKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                x_play.Focus(); // This will cause the LostFocus event to fire which will update the position
            }
        }

        private void x_position_LostFocus(object sender, RoutedEventArgs e)
        {
            TimeSpan ts;
            if (TryParseTimespan(x_position.Text, out ts))
            {
                if (x_player.NaturalDuration.HasTimeSpan &&
                    ts > x_player.NaturalDuration.TimeSpan)
                {
                    ts = x_player.NaturalDuration.TimeSpan;
                }

                x_player.Position = ts;
                UpdatePositionDisplay();
                PlayMomentarily();
            }
            else
            {
                x_position.Text = FormatTimespan(x_player.Position);
            }
        }

        private void x_segmentNumber_LostFocus(object sender, RoutedEventArgs e)
        {
            int segment;
            if (int.TryParse(x_segmentNumber.Text, out segment))
            {
                --segment;  // Change to zero basis
                if (segment < 0) segment = 0;
                if (segment >= m_segments.Count) segment = m_segments.Count - 1;
                if (SetCurrentSegment(segment))
                {
                    PlayMomentarily();
                }
            }
        }

        private void PlayMomentarily()
        {
            if (m_playerIsPlaying) return;
            m_pausePosition = x_player.Position;
            Task.Run((Action)PauseAfterAMoment);
            x_player.IsMuted = true;
            Play();
        }

        private void PauseAfterAMoment()
        {
            System.Threading.Thread.Sleep(100);
            Dispatcher.BeginInvoke((Action)(() =>
            {
                Pause();
                x_player.Position = m_pausePosition;
                x_player.IsMuted = false;
            }), DispatcherPriority.ContextIdle);
        }

        private void x_player_MediaEnded(object sender, RoutedEventArgs e)
        {
            m_playerIsPlaying = false;
        }

        private bool SetCurrentSegment(int segment)
        {
            if (m_playerIsPlaying)
            {
                Pause();
            }
            // Tolerate out-of-range
            if (segment < 0) return false;
            if (segment >= m_segments.Count) return false;
            if (m_currentSegment == segment) return false;

            SaveSegmentInfo();
            m_currentSegment = segment;
            LoadSegmentInfo();

            return true;
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (SetCurrentSegment(m_currentSegment - 1))
            {
                SeekAndSample(m_segments[m_currentSegment].Position);
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (SetCurrentSegment(m_currentSegment + 1))
            {
                // Carry over date and subject if they are blank on the new segment
                if (m_segments[m_currentSegment].Date == DateTime.MinValue)
                {
                    x_date.SelectedDate = m_segments[m_currentSegment - 1].Date;
                }
                if (string.IsNullOrEmpty(m_segments[m_currentSegment].Subject))
                {
                    x_subject.Text = m_segments[m_currentSegment - 1].Subject;
                }

                SeekAndSample(m_segments[m_currentSegment].Position);
            }
        }

        private void PlaySegment_Click(object sender, RoutedEventArgs e)
        {
            Play(m_segments[m_currentSegment].Position, GetSegmentEnd(m_currentSegment));
        }

        private void Replay_Click(object sender, RoutedEventArgs e)
        {
            SeekAndSample(m_segments[m_currentSegment].Position);
        }

        private void ReplayLonger_Click(object sender, RoutedEventArgs e)
        {
            SeekAndSample(m_segments[m_currentSegment].Position, TimeSpan.FromSeconds(3));
        }

        private void PrevScene_Click(object sender, RoutedEventArgs e)
        {
            var segment = m_firstSegmentInScene;
            while (segment > 0)
            {
                --segment;
                if (m_segments[segment].Disposition != SegmentDisposition.AddToPrevious)
                    break;
            }
            if (SetCurrentSegment(segment))
            {
                PlayMomentarily();
            }
        }

        private void NextScene_Click(object sender, RoutedEventArgs e)
        {
            if (SetCurrentSegment(m_lastSegmentInScene + 1))
            {
                PlayMomentarily();
            }
        }

        private void PlayScene_Click(object sender, RoutedEventArgs e)
        {
            Play(m_segments[m_firstSegmentInScene].Position, GetSegmentEnd(m_lastSegmentInScene));
        }

        private void x_disposition_Changed(object sender, RoutedEventArgs e)
        {
            if (x_dispKeep.IsChecked == true)
            {
                x_date.IsEnabled = true;
                x_date.Background = Brushes.Transparent;
                x_subject.IsEnabled = true;
                x_subject.Background = Brushes.Transparent;
                x_title.IsEnabled = true;
                x_title.Background = Brushes.Transparent;
            }
            else
            {
                x_date.IsEnabled = false;
                x_date.Background = Brushes.LightGray;
                x_subject.IsEnabled = false;
                x_subject.Background = Brushes.LightGray;
                x_title.IsEnabled = false;
                x_title.Background = Brushes.LightGray;
            }
            UpdateSceneDisplay();
        }

        private TimeSpan GetSegmentEnd(int segmentIndex)
        {
            return (segmentIndex < m_segments.Count - 1)
                ? m_segments[segmentIndex + 1].Position
                : m_endOfRecording;
        }

        private void x_player_MediaOpened(object sender, RoutedEventArgs e)
        {
            m_endOfRecording = x_player.NaturalDuration.HasTimeSpan
                ? x_player.NaturalDuration.TimeSpan
                : TimeSpan.FromHours(3);
            x_fileDuration.Content = FormatTimespan(m_endOfRecording);
        }

        private void CopyPrev_Click(object sender, RoutedEventArgs e)
        {
            if (m_currentSegment == 0) return;
            int seg;
            for (seg = m_currentSegment-1; seg > 0; --seg)
            {
                if (m_segments[seg].Disposition == SegmentDisposition.Keep) break;
            }
            if (m_segments[seg].Disposition != SegmentDisposition.Keep) return;

            if (m_segments[seg].Date > DateTime.MinValue)
            {
                x_date.SelectedDate = m_segments[seg].Date;
            }
            if (!string.IsNullOrEmpty(m_segments[seg].Subject))
            {
                x_subject.Text = m_segments[seg].Subject;
            }
        }
    }
}
