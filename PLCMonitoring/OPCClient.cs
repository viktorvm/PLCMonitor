using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

using Opc.Da;

namespace PLCMonitoring
{
    class OPCClient
    {
        private Server _opcServer;
        private string _hostName = "localhost";
        private string _opcServerVendor = "RSLinx Remote OPC Server";
        private int _updateRate = 30000;
        private PLC _plc;
        private List<Item> _scadaItems;
        private Subscription _scadaSubscription;
        private SubscriptionState _subscriptionState;

        Opc.IRequest _request;
        //поток,в котором будет выполняться опрос контроллера
        Thread _readThread;

        public OPCClient(PLC plc)
        {
            _plc = plc;

            //инициализируем объект Server
            string scadaUrl = string.Format("opcda://{0}/{1}", _hostName, _opcServerVendor);
            _opcServer = new Opc.Da.Server(new OpcCom.Factory(), new Opc.URL(scadaUrl));

            //создаем список тегов
            _scadaItems = new List<Opc.Da.Item>();

            foreach (string tag in _plc.StatusTags.Keys)
            {
                Opc.Da.Item item = new Opc.Da.Item()
                {
                    ItemName = tag,
                    Active = true,
                    ActiveSpecified = true
                };
                _scadaItems.Add(item);
            }
        }

        public void Connect()
        {
            try
            {
                //подключаемся к серверу
                _opcServer.Connect(new Opc.ConnectData(new NetworkCredential()));
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка подключения к серверу " + string.Format("opcda://{0}/{1}", _hostName, _opcServerVendor));
            }
            //если подключен успешно
            if (_opcServer.IsConnected)
            {
                //создаем подписку
                _subscriptionState = new Opc.Da.SubscriptionState()
                {
                    Active = true,
                    UpdateRate = _updateRate,
                    Deadband = 0,
                    Name = "PLCMonitor_to_" + _plc.Topic
                };
                _scadaSubscription = (Opc.Da.Subscription)_opcServer.CreateSubscription(_subscriptionState);

                //добавляем теги в подписку
                Opc.Da.ItemResult[] result = _scadaSubscription.AddItems(_scadaItems.ToArray());
                for (int i = 0; i < result.Length; i++)
                {
                    _scadaItems[i].ServerHandle = result[i].ServerHandle;
                }

                _scadaSubscription.State.Active = true;

                //запрашиваем данные пока не подана команда _stop
                _readThread = new Thread(new ThreadStart(ReadData));
                _readThread.Start();
            }
            else
            {
                throw new Exception("Ошибка подключения к серверу " + string.Format("opcda://{0}/{1}", _hostName, _opcServerVendor));
            }
        }

        private void ReadData()
        {
            while (true)
            {
                if (_readThread.ThreadState != ThreadState.Running)
                    return;
                try { _scadaSubscription.Read(_scadaItems.ToArray(), 123, group_DataReadDone, out _request); }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
                Thread.Sleep(_updateRate);
            }
        }

        private void group_DataReadDone(object clientHandle, ItemValueResult[] results)
        {
            try
            {
                //обходим каждое измененное значение
                foreach (ItemValueResult item in results)
                {
                    _plc.StatusTags[item.ItemName] = Convert.ToInt16(item.Value);
                }
                //определяем режим работы контроллера
                _plc.DefineMode();
            }
            catch (Exception ex)
            {
                //тут пусто
                //чтобы опрос продолжался, пока контроллер опять не появится
                    ////--тут что то  не чисто, при потере связи все значения уходят в ноль, никакого исключения не возникает
                    ////надо разобраться
            }
        }

        /// <summary>
        /// Прекращает опрос OPC-сервера
        /// </summary>
        public void Stop()
        {
            if (_opcServer != null)
            {

                if (_opcServer.IsConnected)
                {
                    foreach (Subscription sub in _opcServer.Subscriptions)
                    {
                        if (sub != null)
                            _opcServer.CancelSubscription(sub);
                    }
                    _opcServer.Disconnect();
                }

                    _opcServer.Dispose();
            }

            if (_readThread.ThreadState == ThreadState.Running || _readThread.ThreadState == ThreadState.WaitSleepJoin)
                _readThread.Abort();
        }
    }
}
