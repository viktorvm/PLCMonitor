using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

using MainSms;
using Hardcodet.Wpf.TaskbarNotification;

namespace PLCMonitoring
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<PLC> _plcList;
        private string _smsSender { get { return "Plcmonitor"; } }
        private string _smsRecipients { get { return "7982xxxxxxx,7982xxxxxxx"; } } //через запятую
        //проверяет текущее время и по утру отправляет смски о ночной потере связи с контроллером
        private System.Timers.Timer _timer;

        public MainWindow()
        {
            InitializeComponent();

            //хитрая система для сворачивания\разворачивания окна
            ChangeWindowStateCommand com = new ChangeWindowStateCommand();
            com.CommandExecuted += (s, e) =>
            {
                if (this.WindowState == System.Windows.WindowState.Minimized)
                    this.WindowState = System.Windows.WindowState.Normal;
            };
            notifyIcon.DoubleClickCommand = com;

            _timer = new System.Timers.Timer(60000) { AutoReset = true };
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (DateTime.Now.Hour > 6 && DateTime.Now.Hour < 23)
            {
                Dictionary<string, string> lostPlcs = new Dictionary<string, string>();
                foreach (PLC plc in _plcList)
                {
                    if (plc.ConnectionLost && !plc.LostConnectionSmsSended)
                    {
                        lostPlcs.Add(plc.Topic, plc.LostConnectionTime);
                        plc.LostConnectionSmsSended = true;
                    }
                }
                if (lostPlcs.Keys.Count > 0)
                {
                    System.Text.StringBuilder str = new System.Text.StringBuilder();
                    str.Append("Доброе утро. Потери связи за ночь:");
                    foreach (string p in lostPlcs.Keys)
                    {
                        str.AppendLine(lostPlcs[p] + " - " + p);
                    }
                    SendSms(str.ToString());
                }
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == System.Windows.WindowState.Minimized)
                ShowInTaskbar = false;
            else
                ShowInTaskbar = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            List<string> plcsToMonitor = new List<string>();
            plcsToMonitor.Add("MASTER");

            plcsToMonitor.Add("DNS2N");
            plcsToMonitor.Add("DNS6N");
            plcsToMonitor.Add("DNS9N");
            plcsToMonitor.Add("NPU100");
            plcsToMonitor.Add("UPN2");
            plcsToMonitor.Add("CPS");
            plcsToMonitor.Add("UPNTAG");
            plcsToMonitor.Add("UPSVZV");

            plcsToMonitor.Add("KNS2");
            plcsToMonitor.Add("KNS6_N");
            plcsToMonitor.Add("KNS7A");
            plcsToMonitor.Add("KNS7A_N");
            plcsToMonitor.Add("KNS9");
            plcsToMonitor.Add("KNSTAG");
            plcsToMonitor.Add("KNSZV");
            plcsToMonitor.Add("KNS4AN");
            plcsToMonitor.Add("KNS4B");

            //plcsToMonitor.Add("PP3ZV");

            _plcList = new List<PLC>();

            foreach (string plcName in plcsToMonitor)
            {
                PLC plc = new PLC(plcName, PLCFamily.SLC);
                plc.PLCModeChanged += plc_PLCModeChanged;
                plc.PLCFaultChanged += plc_PLCFaultChanged;
                plc.StartMonitoring();

                _plcList.Add(plc);
            }

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        void plc_PLCFaultChanged(object sender, PLCFaultChangedEventArgs args)
        {
            if (args.Faulted)
            {
                AddLog(args.Time + " : " + args.PLC.Topic + " вылетел в ОШИБКУ! Режим работы - " + args.PLC.Mode.ToString());
                SendSms(args.Time + " : " + args.PLC.Topic + " вылетел в ОШИБКУ! Режим работы - " + args.PLC.Mode.ToString());
            }
            else
            {
                AddLog(args.Time + " : На " + args.PLC.Topic + " ошибка устранена! Режим работы - " + args.PLC.Mode.ToString());
                SendSms(args.Time + " : На " + args.PLC.Topic + " ошибка устранена! Режим работы - " + args.PLC.Mode.ToString());
            }
        }

        void plc_PLCModeChanged(object sender, PLCModeChangedEventArgs args)
        {
            if (args.PLC.FirstPass)
            {
                AddLog(args.Time + " : " + args.PLC.Topic + " переведен в " + args.PLC.Mode.ToString());

                args.PLC.FirstPass = false;
            }
            else
            {
                if (args.PLC.Mode == PLCMode.RemoteDownloadInProgress)
                {
                    AddLog(args.Time + " : Потеря связи с " + args.PLC.Topic + " в " + args.PLC.LostConnectionTime);

                    if (DateTime.Now.Hour > 6 && DateTime.Now.Hour < 23)
                    {
                        SendSms("Потеря связи с " + args.PLC.Topic + " в " + args.PLC.LostConnectionTime);
                        args.PLC.LostConnectionSmsSended = true;
                    }
                }
                else
                {
                    AddLog(args.Time + " : " + args.PLC.Topic + " переведен в " + args.PLC.Mode.ToString());

                    if (DateTime.Now.Hour > 6 && DateTime.Now.Hour < 23)
                    {
                        SendSms(args.Time + " : " + args.PLC.Topic + " переведен в " + args.PLC.Mode.ToString());
                    }
                }
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            foreach (PLC plc in _plcList)
                plc.StopMonitoring();

            btnStop.IsEnabled = false;
            btnStart.IsEnabled = true;
        }
        private void ShowCustomBalloon()
        {
            //notifyIcon.ShowCustomBalloon(baloon, System.Windows.Controls.Primitives.PopupAnimation.Slide, 10000);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (PLC plc in _plcList)
                plc.StopMonitoring();
        }

        private void SendSms(string text)
        {
            Mainsms sms = new Mainsms("PLCMonitor", "8e19651e07f2e");
            ResponseSend rsend = sms.send(_smsSender, _smsRecipients, text);
            DateTime time = DateTime.Now;
            string timeStr = time.Hour + ":" + time.Minute + " " + time.Day + "." + time.Month;
            if (rsend.status == "success")
            {
                AddLog(timeStr + ": Смс-уведомление успешно отправлено.");
            }
            else
            {
                AddLog(timeStr + ": Не удалось отправить смс-уведомление.");
            }
        }

        public void AddLog(string message)
        {
            tbLog.Dispatcher.Invoke((Action)(() =>
            {
                tbLog.Text += message + Environment.NewLine;
            }));
        }
    }

    public class ChangeWindowStateCommand : System.Windows.Input.ICommand
    {
        public void Execute(object parameter)
        {
            EventArgs e = new EventArgs();
            OnCommandExecuted(e);
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged;

        public event EventHandler CommandExecuted;

        protected virtual void OnCommandExecuted(EventArgs e)
        {
            EventHandler handler = CommandExecuted;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    } 
}
