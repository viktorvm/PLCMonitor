using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PLCMonitoring
{
    delegate void PLCModeChangedEventHandler(object sender, PLCModeChangedEventArgs args);
    delegate void PLCFaultChangedEventHandler(object sender, PLCFaultChangedEventArgs args);

    class PLC
    {
        OPCClient _monitor;
        private string _topic;
        private PLCFamily _family;
        private PLCMode _mode;
        private bool _faulted;
        private Dictionary<string, short> _statusTags;

        //чтобы не отправлять смс при первом срабатывании
        private bool _firstPass;
        //чтобы избежать ложных срабатываний события "потеря связи"
        private short _lostConnectionCount;
        //время потери связи
        private DateTime _lostConnectionTime;

        #region Это чтобы не будили меня ночные смски
        //true если связи с контроллеом нет
        private bool _connectionLost;
        //true если уведомление о петере связи отправлено
        private bool _lostConnectionSmsSended;
        #endregion

        public PLC(string topic, PLCFamily family)
        {
            _topic = topic;
            _family = family;
            _firstPass = true;
            _faulted = false;
            _lostConnectionCount = 0;
            _lostConnectionTime = new DateTime(1901, 01, 01, 01, 01, 01);

            _statusTags = new Dictionary<string, short>();
            if (_family == PLCFamily.SLC)
            {
                _statusTags.Add("[" + _topic + "]" + "S2:1/0", 2);
                _statusTags.Add("[" + _topic + "]" + "S2:1/1", 2);
                _statusTags.Add("[" + _topic + "]" + "S2:1/2", 2);
                _statusTags.Add("[" + _topic + "]" + "S2:1/3", 2);
                _statusTags.Add("[" + _topic + "]" + "S2:1/4", 2);
                _statusTags.Add("[" + _topic + "]" + "S2:1/13", 2);
            }
        }

        #region Глобальные переменные
        public string Topic { get { return _topic; } }
        public PLCFamily Family { get { return _family; } }
        public PLCMode Mode
        {
            get { return _mode; }
            set
            {
                _lostConnectionCount = 0;

                if (value == _mode)
                    return;

                _mode = value;

                if (_mode == PLCMode.RemoteDownloadInProgress)
                {
                    _connectionLost = true;
                    PLCModeChangedEventArgs e = new PLCModeChangedEventArgs(this, DateTime.Now);
                    OnPLCModeChanged(e);
                }
                else
                {
                    _connectionLost = false;
                    PLCModeChangedEventArgs e = new PLCModeChangedEventArgs(this, DateTime.Now);
                    OnPLCModeChanged(e);
                }
            }
        }
        public bool Faulted
        {
            get { return _faulted; }
            set
            {
                if (value == _faulted)
                    return;

                _faulted = value;

                PLCFaultChangedEventArgs e = new PLCFaultChangedEventArgs(this, value);
                OnPLCFaultChanged(e);
            }
        }
        public Dictionary<string, short> StatusTags
        {
            get { return _statusTags; }
        }
        public short this[string TKey]
        {
            get { return _statusTags[TKey]; }
            set
            {
                _statusTags[TKey] = value;
            }
        }
        public bool FirstPass
        {
            get { return _firstPass; }
            set { _firstPass = value; }
        }
        public string LostConnectionTime
        {
            get { return _lostConnectionTime.Hour + ":" + _lostConnectionTime.Minute + " " + _lostConnectionTime.Day + "." + _lostConnectionTime.Month; }
        }
        public bool ConnectionLost
        {
            get { return _connectionLost; }
        }
        public bool LostConnectionSmsSended
        {
            get { return _lostConnectionSmsSended; }
            set { _lostConnectionSmsSended = value; }
        }
        #endregion

        #region События
        public event PLCModeChangedEventHandler PLCModeChanged;
        protected virtual void OnPLCModeChanged(PLCModeChangedEventArgs e)
        {
            PLCModeChangedEventHandler handler = PLCModeChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        public event PLCFaultChangedEventHandler PLCFaultChanged;
        protected virtual void OnPLCFaultChanged(PLCFaultChangedEventArgs e)
        {
            PLCFaultChangedEventHandler handler = PLCFaultChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        #endregion

        public void StartMonitoring()
        {
            _monitor = new OPCClient(this);
            _monitor.Connect();
        }
        public void StopMonitoring()
        {
            _monitor.Stop();
        }

        /// <summary>
        /// Определяет режим работы исходя из комбинации статус-бит
        /// </summary>
        public void DefineMode()
        {
            StringBuilder statusBits = new StringBuilder();
            statusBits.Append(_statusTags["[" + _topic + "]" + "S2:1/4"]);
            statusBits.Append(_statusTags["[" + _topic + "]" + "S2:1/3"]);
            statusBits.Append(_statusTags["[" + _topic + "]" + "S2:1/2"]);
            statusBits.Append(_statusTags["[" + _topic + "]" + "S2:1/1"]);
            statusBits.Append(_statusTags["[" + _topic + "]" + "S2:1/0"]);

            //определяем режим работы
            switch (statusBits.ToString())
            {
                //когда пропадает связь с контроллером, запрос возвращает нули
                //поэтому принимаем режим работа 00000 "RemoteDownloadInProgress" за потерю связи
                case "00000":
                    //если соединение уже потеряно, не отрабатывать
                    if (_connectionLost)
                        return;

                    //поскольку со связью беда
                    //для отсечения ложного срабатывания, получим статус "потеря связи" 10 раза прежде чем отправить смс
                    //выходит задержка в оповещении 10x_updateRate (не критично)
                    if (_lostConnectionCount == 0)
                        _lostConnectionTime = DateTime.Now;

                    _lostConnectionCount++;

                    if (_lostConnectionCount == 10)
                    {
                        this.Mode = PLCMode.RemoteDownloadInProgress;
                    }

                    break;
                case "00001":
                    this.Mode = PLCMode.RemoteProgram;
                    break;
                case "00011":
                    this.Mode = PLCMode.SuspendIdle;
                    break;
                case "00110":
                    this.Mode = PLCMode.RemoteRun;
                    break;
                case "00111":
                    this.Mode = PLCMode.RemoteTestContinuous;
                    break;
                case "01000":
                    this.Mode = PLCMode.RemoteTestSingleScan;
                    break;
                case "01001":
                    this.Mode = PLCMode.RemoteTestSingleStep;
                    break;
                case "10000":
                    this.Mode = PLCMode.DownloadInProgress;
                    break;
                case "10001":
                    this.Mode = PLCMode.ProgramMode;
                    break;
                case "11011":
                    this.Mode = PLCMode.SuspendIdle2;
                    break;
                case "11110":
                    this.Mode = PLCMode.Run;
                    break;
                default:
                    this.Mode = PLCMode.Undefiend;
                    break;
            }

            //определяем вылетел ли в ошибку
            //об этом оповещаем мгновенно
            if (_statusTags["[" + _topic + "]" + "S2:1/13"] == 1)
                this.Faulted = true;
            else
                this.Faulted = false;
        }
    }


    public enum PLCFamily { SLC, ControlLogic, CompacLogic }
    public enum PLCMode
    {
        RemoteDownloadInProgress, RemoteProgram, SuspendIdle, RemoteRun, RemoteTestContinuous, RemoteTestSingleScan,
        RemoteTestSingleStep, DownloadInProgress, ProgramMode, SuspendIdle2, Run, Undefiend
    }

    class PLCModeChangedEventArgs
    {
        private PLC _plc;
        private DateTime _time;

        public PLCModeChangedEventArgs(PLC plc, DateTime eventTime)
        {
            _plc = plc;
            _time = eventTime;
        }

        /// <summary>
        /// Контроллер, сгенерировавший событие
        /// </summary>
        public PLC PLC { get { return _plc; } }
        /// <summary>
        /// Время генерации события
        /// </summary>
        public string Time { get { return _time.Hour + ":" + _time.Minute + " " + _time.Day + "." + _time.Month; } }
    }
    class PLCFaultChangedEventArgs
    {
        /// <summary>
        /// Контроллер, сгенерировавший событие
        /// </summary>
        private PLC _plc;
        /// <summary>
        /// Указывает находися ли контроллер в ошибке
        /// </summary>
        private bool _faulted;
        /// <summary>
        /// Время генерации события
        /// </summary>
        private DateTime _time;

        public PLCFaultChangedEventArgs(PLC plc, bool faulted)
        {
            _plc = plc;
            _faulted = faulted;
            _time = DateTime.Now;
        }

        public PLC PLC { get { return _plc; } }
        public bool Faulted { get { return _faulted; } }
        public string Time { get { return _time.Hour + ":" + _time.Minute + " " + _time.Day + "." + _time.Month; } }
    }
}
