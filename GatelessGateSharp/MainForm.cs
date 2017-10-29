﻿// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SQLite;
using System.Collections;
using System.Runtime.InteropServices;
using Cloo;
using ATI.ADL;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;



namespace GatelessGateSharp
{
    unsafe public partial class MainForm : Form
    {
        [DllImport("phymem_wrapper.dll")]
        extern public static int LoadPhyMemDriver();
        [DllImport("phymem_wrapper.dll")]
        extern public static void UnloadPhyMemDriver();

        private static MainForm instance;
        public static String shortAppName = "Gateless Gate Sharp";
        public static String appVersion = "0.0.4";
        public static String appName = shortAppName + " " + appVersion + " alpha";
        private static String databaseFileName = "GatelessGateSharp.sqlite";
        private static String logFileName = "GatelessGateSharp.log";
        private System.Threading.Mutex loggerMutex = new System.Threading.Mutex();
        private Control[] labelGPUVendorArray;
        private Control[] labelGPUNameArray;
        private Control[] labelGPUIDArray;
        private Control[] labelGPUSpeedArray;
        private Control[] labelGPUTempArray;
        private Control[] labelGPUActivityArray;
        private Control[] labelGPUFanArray;
        private Control[] labelGPUCoreClockArray;
        private Control[] labelGPUMemoryClockArray;
        private ComputeDevice[] computeDeviceArray;
        private const int computeDeviceArrayMaxLength = 8; // This depends on MainForm.
        private bool ADLInitialized = false;
        private bool NVMLInitialized = false;
        private Int32[] ADLAdapterIndexArray;
        private System.Threading.Mutex DeviceManagementLibrariesMutex = new System.Threading.Mutex();
        private ManagedCuda.Nvml.nvmlDevice[] nvmlDeviceArray;

        private bool mDevFee = true;
        private int mDevFeePercentage = 1;
        private int mDevFeeDurationInSeconds = 60;
        private String mDevFeeBitcoinAddress = "1BHwDWVerUTiKxhHPf2ubqKKiBMiKQGomZ";

        public static MainForm Instance { get { return instance; }}

        public static void Logger(String lines)
        {
            Instance.loggerMutex.WaitOne();
            System.IO.StreamWriter file = new System.IO.StreamWriter(logFileName, true);
            file.WriteLine(lines);
            file.Close();
            Instance.loggerMutex.ReleaseMutex();

            try
            {
                Instance.richTextBoxLog.Invoke((MethodInvoker)delegate
                {
                    Instance.loggerMutex.WaitOne();
                    Instance.richTextBoxLog.SelectionLength = 0;
                    Instance.richTextBoxLog.SelectionStart = Instance.richTextBoxLog.Text.Length;
                    Instance.richTextBoxLog.ScrollToCaret();
                    if (Instance.richTextBoxLog.Text != "")
                        Instance.richTextBoxLog.Text += "\n";
                    Instance.richTextBoxLog.Text += lines;
                    Instance.richTextBoxLog.SelectionLength = 0;
                    Instance.richTextBoxLog.SelectionStart = Instance.richTextBoxLog.Text.Length;
                    Instance.richTextBoxLog.ScrollToCaret();
                    Instance.loggerMutex.ReleaseMutex();
                });
            }
            catch (Exception ex) { }
        }

        unsafe public MainForm()
        {
            instance = this;

            InitializeComponent();
        }
        
        private void CreateNewDatabase()
        {
            SQLiteConnection.CreateFile(databaseFileName);
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;");
            conn.Open();
            String sql = "create table wallet_addresses (coin varchar(128), address varchar(128));";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.ExecuteNonQuery();
            sql = "create table pools (name varchar(128));";
            command = new SQLiteCommand(sql, conn);
            command.ExecuteNonQuery();
            conn.Close();
        }

        private void LoadDatabase()
        {
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;");
            conn.Open();
            String sql = "select * from wallet_addresses";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if ((String)reader["coin"] == "bitcoin")
                {
                    textBoxBitcoinAddress.Text = (String)reader["address"];
                }
                else if ((String)reader["coin"] == "ethereum")
                {
                    textBoxEthereumAddress.Text = (String)reader["address"];
                }
                else if ((String)reader["coin"] == "monero")
                {
                    textBoxMoneroAddress.Text = (String)reader["address"];
                }
                else if ((String)reader["coin"] == "zcash")
                {
                    textBoxZcashAddress.Text = (String)reader["address"];
                }
            }

            try
            {
                sql = "select * from pools";
                command = new SQLiteCommand(sql, conn);
                reader = command.ExecuteReader();
                List<String> oldItems = new List<string>();
                foreach (String poolName in listBoxPoolPriorities.Items)
                    oldItems.Add(poolName);
                listBoxPoolPriorities.Items.Clear();
                while (reader.Read())
                    listBoxPoolPriorities.Items.Add((String)reader["name"]);
                foreach (String poolName in oldItems)
                    if (!listBoxPoolPriorities.Items.Contains(poolName))
                        listBoxPoolPriorities.Items.Add(poolName);
            }
            catch (Exception ex) { }

            conn.Close();
        }

        private void UpdateDatabase()
        {
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + databaseFileName + ";Version=3;");
            conn.Open();
            String sql = "delete from wallet_addresses";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.ExecuteNonQuery();

            sql = "insert into wallet_addresses (coin, address) values (@coin, @address)";
            command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@coin", "bitcoin");
            command.Parameters.AddWithValue("@address", textBoxBitcoinAddress.Text);
            command.ExecuteNonQuery();
            command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@coin", "ethereum");
            command.Parameters.AddWithValue("@address", textBoxEthereumAddress.Text);
            command.ExecuteNonQuery();
            command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@coin", "monero");
            command.Parameters.AddWithValue("@address", textBoxMoneroAddress.Text);
            command.ExecuteNonQuery();
            command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@coin", "zcash");
            command.Parameters.AddWithValue("@address", textBoxZcashAddress.Text);
            command.ExecuteNonQuery();

            try
            {
                sql = "delete from pools";
                command = new SQLiteCommand(sql, conn);
                command.ExecuteNonQuery();
            }
            catch (Exception ex) {
                sql = "create table pools (name varchar(128));";
                command = new SQLiteCommand(sql, conn);
                command.ExecuteNonQuery();
            }

            sql = "insert into pools (name) values (@name)";
            foreach (String poolName in listBoxPoolPriorities.Items)
            {
                command = new SQLiteCommand(sql, conn);
                command.Parameters.AddWithValue("@name", poolName);
                command.ExecuteNonQuery();
            }
            
            conn.Close();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Logger(appName + " started.");
            labelGPUVendorArray = new Control[] { labelGPU0Vendor, labelGPU1Vendor, labelGPU2Vendor, labelGPU3Vendor, labelGPU4Vendor, labelGPU5Vendor, labelGPU6Vendor, labelGPU7Vendor };
            labelGPUNameArray = new Control[] { labelGPU0Name, labelGPU1Name, labelGPU2Name, labelGPU3Name, labelGPU4Name, labelGPU5Name, labelGPU6Name, labelGPU7Name };
            labelGPUIDArray = new Control[] { labelGPU0ID, labelGPU1ID, labelGPU2ID, labelGPU3ID, labelGPU4ID, labelGPU5ID, labelGPU6ID, labelGPU7ID };
            labelGPUTempArray = new Control[] { labelGPU0Temp, labelGPU1Temp, labelGPU2Temp, labelGPU3Temp, labelGPU4Temp, labelGPU5Temp, labelGPU6Temp, labelGPU7Temp };
            labelGPUActivityArray = new Control[] { labelGPU0Activity, labelGPU1Activity, labelGPU2Activity, labelGPU3Activity, labelGPU4Activity, labelGPU5Activity, labelGPU6Activity, labelGPU7Activity };
            labelGPUFanArray = new Control[] { labelGPU0Fan, labelGPU1Fan, labelGPU2Fan, labelGPU3Fan, labelGPU4Fan, labelGPU5Fan, labelGPU6Fan, labelGPU7Fan };
            labelGPUSpeedArray = new Control[] { labelGPU0Speed, labelGPU1Speed, labelGPU2Speed, labelGPU3Speed, labelGPU4Speed, labelGPU5Speed, labelGPU6Speed, labelGPU7Speed };
            labelGPUCoreClockArray = new Control[] { labelGPU0CoreClock, labelGPU1CoreClock, labelGPU2CoreClock, labelGPU3CoreClock, labelGPU4CoreClock, labelGPU5CoreClock, labelGPU6CoreClock, labelGPU7CoreClock };
            labelGPUMemoryClockArray = new Control[] { labelGPU0MemoryClock, labelGPU1MemoryClock, labelGPU2MemoryClock, labelGPU3MemoryClock, labelGPU4MemoryClock, labelGPU5MemoryClock, labelGPU6MemoryClock, labelGPU7MemoryClock };

            if (LoadPhyMemDriver() != 0)
            {
                Logger("Successfully loaded phymem.");
            }
            else
            {
                Logger("Failed to load phymem.");
                MessageBox.Show("Failed to load phymem.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Environment.Exit(1);
            }

            if (!System.IO.File.Exists(databaseFileName))
                CreateNewDatabase();
            LoadDatabase();
            InitializeDevices();

            Text = appName;
        }

        private void InitializeDevices()
        {
            ArrayList computeDeviceArrayList = new ArrayList();

            foreach (ComputePlatform platform in ComputePlatform.Platforms)
            {
                IList<ComputeDevice> devices = platform.Devices;
                ComputeContextPropertyList properties = new ComputeContextPropertyList(platform);
                ComputeContext context = new ComputeContext(devices, properties, null, IntPtr.Zero);

                foreach (ComputeDevice device in context.Devices)
                {
                    if (device.Vendor == "Intel Corporation"
                        || device.Vendor == "GenuineIntel"
                        || device.Type == ComputeDeviceTypes.Cpu)
                        continue;
                    computeDeviceArrayList.Add(device);
                }
            }
            computeDeviceArray = Array.ConvertAll(computeDeviceArrayList.ToArray(), item => (Cloo.ComputeDevice)item);
            Logger("Number of Devices: " + computeDeviceArray.Length);

            int index = 0;
            foreach (ComputeDevice device in computeDeviceArray)
            {
                labelGPUVendorArray[index].Text = (device.Vendor == "Advanced Micro Devices, Inc.") ? "AMD" :
                                                  (device.Vendor == "NVIDIA Corporation") ? "NVIDIA" :
                                                  (device.Vendor == "Intel Corporation") ? "Intel" :
                                                  (device.Vendor == "GenuineIntel") ? "Intel" :
                                                  device.Vendor;
                labelGPUNameArray[index].Text = device.Name;

                labelGPUSpeedArray[index].Text = "-";
                labelGPUActivityArray[index].Text = "-";
                labelGPUTempArray[index].Text = "-";
                labelGPUFanArray[index].Text = "-";
 
                ++index;
            }

            for (; index < computeDeviceArrayMaxLength; ++index)
            {
                labelGPUVendorArray[index].Visible = false;
                labelGPUNameArray[index].Visible = false;
                labelGPUIDArray[index].Visible = false;
                labelGPUSpeedArray[index].Visible = false;
                labelGPUActivityArray[index].Visible = false;
                labelGPUTempArray[index].Visible = false;
                labelGPUFanArray[index].Visible = false;
                labelGPUCoreClockArray[index].Visible = false;
                labelGPUMemoryClockArray[index].Visible = false;
            }

            int ADLRet = -1;
            int NumberOfAdapters = 0;
            ADLAdapterIndexArray = new Int32[computeDeviceArray.Length];
            for (int i = 0; i < computeDeviceArray.Length; i++)
                ADLAdapterIndexArray[i] = -1;
            if (null != ADL.ADL_Main_Control_Create)
                ADLRet = ADL.ADL_Main_Control_Create(ADL.ADL_Main_Memory_Alloc, 1);
            if (ADL.ADL_SUCCESS == ADLRet)
            {
                Logger("Successfully initialized AMD Display Library.");
                ADLInitialized = true;
                if (null != ADL.ADL_Adapter_NumberOfAdapters_Get)
                {
                    ADL.ADL_Adapter_NumberOfAdapters_Get(ref NumberOfAdapters);
                }
                Logger("Number of ADL Adapters: " + NumberOfAdapters.ToString());

                if (0 < NumberOfAdapters)
                {
                    ADLAdapterInfoArray OSAdapterInfoData;
                    OSAdapterInfoData = new ADLAdapterInfoArray();

                    if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                    {
                        IntPtr AdapterBuffer = IntPtr.Zero;
                        int size = Marshal.SizeOf(OSAdapterInfoData);
                        AdapterBuffer = Marshal.AllocCoTaskMem((int)size);
                        Marshal.StructureToPtr(OSAdapterInfoData, AdapterBuffer, false);

                        if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                        {
                            ADLRet = ADL.ADL_Adapter_AdapterInfo_Get(AdapterBuffer, size);
                            if (ADL.ADL_SUCCESS == ADLRet)
                            {
                                OSAdapterInfoData = (ADLAdapterInfoArray)Marshal.PtrToStructure(AdapterBuffer, OSAdapterInfoData.GetType());
                                int IsActive = 0;

                                int deviceIndex = 0;
                                foreach (ComputeDevice device in computeDeviceArray)
                                {
                                    if (device.Vendor == "Advanced Micro Devices, Inc.")
                                    {
                                        ComputeDevice.cl_device_topology_amd topology = device.TopologyAMD;
                                        for (int i = 0; i < NumberOfAdapters; i++)
                                        {
                                            if (null != ADL.ADL_Adapter_Active_Get)
                                                ADLRet = ADL.ADL_Adapter_Active_Get(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref IsActive);
                                            if (OSAdapterInfoData.ADLAdapterInfo[i].BusNumber == topology.bus
                                                && (ADLAdapterIndexArray[deviceIndex] < 0 || IsActive != 0))
                                            {
                                                ADLAdapterIndexArray[deviceIndex] = OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex;
                                                String adapterName = OSAdapterInfoData.ADLAdapterInfo[i].AdapterName;
                                                adapterName = adapterName.Replace("(TM)", "");
                                                adapterName = adapterName.Replace(" Series", "");
                                                labelGPUNameArray[deviceIndex].Text = adapterName;
                                            }
                                        }
                                    }
                                    ++deviceIndex;
                                }
                            }
                            else
                            {
                                Logger("ADL_Adapter_AdapterInfo_Get() returned error code " + ADLRet.ToString());
                            }
                        }
                        // Release the memory for the AdapterInfo structure
                        if (IntPtr.Zero != AdapterBuffer)
                            Marshal.FreeCoTaskMem(AdapterBuffer);
                    } 
                }
            }
            else
            {
                Logger("Failed to initialize AMD Display Library.");
            }

            try {
                if (ManagedCuda.Nvml.NvmlNativeMethods.nvmlInit() == 0)
                {
                    Logger("Successfully initialized NVIDIA Management Library.");
                    uint nvmlDeviceCount = 0;
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetCount(ref nvmlDeviceCount);
                    Logger("NVML Device Count: " + nvmlDeviceCount);

                    nvmlDeviceArray = new ManagedCuda.Nvml.nvmlDevice[computeDeviceArray.Length];
                    for (uint i = 0; i < nvmlDeviceCount; ++i)
                    {
                        ManagedCuda.Nvml.nvmlDevice nvmlDevice = new ManagedCuda.Nvml.nvmlDevice();
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetHandleByIndex(i, ref nvmlDevice);
                        ManagedCuda.Nvml.nvmlPciInfo info = new ManagedCuda.Nvml.nvmlPciInfo();
                        ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetPciInfo(nvmlDevice, ref info);

                        uint j;
                        for (j = 0; j < computeDeviceArray.Length; ++j) {
                            if (computeDeviceArray[j].Vendor == "NVIDIA Corporation" && computeDeviceArray[j].PciBusIdNV == info.bus) { 
                                nvmlDeviceArray[j] = nvmlDevice;
                                break;
                            }
                        }
                        if (j >= computeDeviceArray.Length)
                            throw new Exception();
                    }

                    NVMLInitialized = true;
                }
            }
            catch (Exception ex)
            {
            }
            if (!NVMLInitialized)
            {
                Logger("Failed to initialize NVIDIA Management Library.");
            }
            else {
            }
            
            UpdateDeviceStatus();
            timerDeviceStatusUpdates.Enabled = true;
            UpdateCurrencyStats();
            timerCurrencyStatUpdates.Enabled = true;
        }

        private class CustomWebClient : System.Net.WebClient
        {
            protected override System.Net.WebRequest GetWebRequest(Uri uri)
            {
                System.Net.WebRequest request = base.GetWebRequest(uri);
                request.Timeout = 5 * 1000;
                return request;
            }
        }

        private void UpdateCurrencyStats()
        {
            try
            {
                double totalSpeed = 0;
                if (mMiners != null)
                    foreach (Miner miner in mMiners)
                        totalSpeed += miner.Speed;
                labelCurrentSpeed.Text = (appState != ApplicationGlobalState.Mining) ? "-" : String.Format("{0:N2} Mh/s", totalSpeed / 1000000);

                String poolName = (string)listBoxPoolPriorities.Items[0];
                if (appState == ApplicationGlobalState.Mining && mDevFee)
                {
                    labelCurrentPool.Text = "DEVFEE";
                    poolName = mStratum.PoolName;
                }
                else if (appState == ApplicationGlobalState.Mining && mStratum != null)
                {
                    labelCurrentPool.Text = mStratum.PoolName + " (" + mStratum.ServerAddress + ")";
                    poolName = mStratum.PoolName;
                }
                else
                {
                    labelCurrentPool.Text = (string)listBoxPoolPriorities.Items[0];
                }

                if (appState == ApplicationGlobalState.Mining && mMiners != null)
                {
                    labelCurrentAlgorithm.Text = mMiners[0].AlgorithmName; // TODO
                }
                else
                {
                    labelCurrentAlgorithm.Text = "-";
                }

                var client = new CustomWebClient();
                double USDBTC = 0;
                {
                    String jsonString = client.DownloadString("https://blockchain.info/ticker");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    var USD = (JContainer)(response["USD"]);
                    USDBTC = (double)(USD["15m"]);
                }

                double USDETH = 0.0;
                {
                    String jsonString = client.DownloadString("https://api.coinmarketcap.com/v1/ticker/?convert=USD");
                    var responseArray = JsonConvert.DeserializeObject<JArray>(jsonString);
                    foreach (JContainer currency in responseArray)
                        if ((String)currency["id"] == "ethereum")
                            USDETH = Double.Parse((String)currency["price_usd"]);
                }

                if (poolName == "NiceHash")
                {
                    double balance = 0;
                    String jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.provider&addr=" + textBoxBitcoinAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    var result = (JContainer)(response["result"]);
                    var stats = (JArray)(result["stats"]);
                    foreach (JContainer item in stats)
                        balance += Double.Parse((String)item["balance"]);
                    labelBalance.Text = String.Format("{0:N6}", balance) + " BTC (" + String.Format("{0:N2}", (balance * USDBTC)) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && textBoxBitcoinAddress.Text != "")
                    {
                        double price = 0;
                        jsonString = client.DownloadString("https://api.nicehash.com/api?method=stats.global.current");
                        response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                        result = (JContainer)(response["result"]);
                        stats = (JArray)(result["stats"]);
                        foreach (JContainer item in stats)
                            if ((double)item["algo"] == 20)
                                price = Double.Parse((String)item["price"]) * totalSpeed / 1000000000.0;

                        labelPriceDay.Text = String.Format("{0:N6}", price) + " BTC/Day (" + String.Format("{0:N2}", (price * USDBTC)) + " USD/Day)";
                        labelPriceWeek.Text = String.Format("{0:N6}", price * 7) + " BTC/Week (" + String.Format("{0:N2}", (price * 7 * USDBTC)) + " USD/Week)";
                        labelPriceMonth.Text = String.Format("{0:N6}", price * (365.25 / 12)) + " BTC/Month (" + String.Format("{0:N2}", (price * (365.25 / 12) * USDBTC)) + " USD/Month)";
                    }
                    else
                    {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                }
                else if (poolName == "ethermine.org" && textBoxEthereumAddress.Text != "")
                {
                    String jsonString = client.DownloadString("https://api.ethermine.org/miner/" + textBoxEthereumAddress.Text + "/currentStats");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    var data = (JContainer)(response["data"]);
                    double balance = (double)data["unpaid"] * 1e-18;
                    double averageHashrate = (double)data["averageHashrate"];
                    double coinsPerMin = (double)data["coinsPerMin"];
                    labelBalance.Text = String.Format("{0:N6}", balance) + " ETH (" + String.Format("{0:N2}", (balance * USDETH)) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && averageHashrate != 0)
                    {
                        double price = (coinsPerMin * 60 * 24) * (totalSpeed / averageHashrate);

                        labelPriceDay.Text = String.Format("{0:N6}", price) + " ETH/Day (" + String.Format("{0:N2}", (price * USDETH)) + " USD/Day)";
                        labelPriceWeek.Text = String.Format("{0:N6}", price * 7) + " ETH/Week (" + String.Format("{0:N2}", (price * 7 * USDETH)) + " USD/Week)";
                        labelPriceMonth.Text = String.Format("{0:N6}", price * (365.25 / 12)) + " ETH/Month (" + String.Format("{0:N2}", (price * (365.25 / 12) * USDETH)) + " USD/Month)";
                    }
                    else
                    {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                }
                else if (poolName == "ethpool.org" && textBoxEthereumAddress.Text != "")
                {
                    String jsonString = client.DownloadString("http://api.ethpool.org/miner/" + textBoxEthereumAddress.Text + "/currentStats");
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    var data = (JContainer)(response["data"]);
                    double balance = 0;
                    try
                    {
                        balance = (double)data["unpaid"] * 1e-18;
                    }
                    catch (Exception ex) { }
                    double averageHashrate = (double)data["averageHashrate"];
                    double coinsPerMin = (double)data["coinsPerMin"];
                    labelBalance.Text = String.Format("{0:N6}", balance) + " ETH (" + String.Format("{0:N2}", (balance * USDETH)) + " USD)";

                    if (appState == ApplicationGlobalState.Mining && averageHashrate != 0)
                    {
                        double price = (coinsPerMin * 60 * 24) * (totalSpeed / averageHashrate);

                        labelPriceDay.Text = String.Format("{0:N6}", price) + " ETH/Day (" + String.Format("{0:N2}", (price * USDETH)) + " USD/Day)";
                        labelPriceWeek.Text = String.Format("{0:N6}", price * 7) + " ETH/Week (" + String.Format("{0:N2}", (price * 7 * USDETH)) + " USD/Week)";
                        labelPriceMonth.Text = String.Format("{0:N6}", price * (365.25 / 12)) + " ETH/Month (" + String.Format("{0:N2}", (price * (365.25 / 12) * USDETH)) + " USD/Month)";
                    }
                    else
                    {
                        labelPriceDay.Text = "-";
                        labelPriceWeek.Text = "-";
                        labelPriceMonth.Text = "-";
                    }
                }
                else if (poolName == "Nanopool" && textBoxEthereumAddress.Text != "")
                {
                    String jsonString = client.DownloadString("https://api.nanopool.org/v1/eth/user/" + textBoxEthereumAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    var data = (JContainer)(response["data"]);
                    double balance = 0;
                    try
                    {
                        balance = (double)data["balance"];
                    }
                    catch (Exception ex) { }
                    labelBalance.Text = String.Format("{0:N6}", balance) + " ETH (" + String.Format("{0:N2}", (balance * USDETH)) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                }
                else if (poolName == "DwarfPool" && textBoxEthereumAddress.Text != "")
                {
                    String jsonString = client.DownloadString("http://dwarfpool.com/eth/api?wallet=" + textBoxEthereumAddress.Text);
                    var response = JsonConvert.DeserializeObject<Dictionary<string, Object>>(jsonString);
                    double balance = 0;
                    try
                    {
                        balance = Double.Parse((String)response["wallet_balance"]);
                    }
                    catch (Exception ex) { }
                    labelBalance.Text = String.Format("{0:N6}", balance) + " ETH (" + String.Format("{0:N2}", (balance * USDETH)) + " USD)";

                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                }
                else
                {
                    labelPriceDay.Text = "-";
                    labelPriceWeek.Text = "-";
                    labelPriceMonth.Text = "-";
                    labelBalance.Text = "-";
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void UpdateDeviceStatus()
        {
            double totalSpeed = 0;
            if (mMiners != null)
                foreach (Miner miner in mMiners)
                    totalSpeed += miner.Speed;
            labelCurrentSpeed.Text = (appState != ApplicationGlobalState.Mining) ? "-" : String.Format("{0:N2} Mh/s", totalSpeed / 1000000);

            DeviceManagementLibrariesMutex.WaitOne();
            int deviceIndex = 0;
            foreach (ComputeDevice device in computeDeviceArray)
            {
                double speed = 0;
                if (mMiners != null)
                    foreach (Miner miner in mMiners)
                        if (miner.DeviceIndex == deviceIndex)
                            speed += miner.Speed;
                labelGPUSpeedArray[deviceIndex].Text = (appState != ApplicationGlobalState.Mining) ? "-" : String.Format("{0:N2} Mh/s", speed / 1000000);

                if (ADLAdapterIndexArray[deviceIndex] >= 0)
                {
                    // temperature
                    ADLTemperature OSADLTemperatureData;
                    OSADLTemperatureData = new ADLTemperature();
                    IntPtr tempBuffer = IntPtr.Zero;
                    int size = Marshal.SizeOf(OSADLTemperatureData);
                    tempBuffer = Marshal.AllocCoTaskMem((int)size);
                    Marshal.StructureToPtr(OSADLTemperatureData, tempBuffer, false);

                    if (null != ADL.ADL_Overdrive5_Temperature_Get)
                    {
                        int ADLRet = ADL.ADL_Overdrive5_Temperature_Get(ADLAdapterIndexArray[deviceIndex], 0, tempBuffer);
                        if (ADL.ADL_SUCCESS == ADLRet)
                        {
                            OSADLTemperatureData = (ADLTemperature)Marshal.PtrToStructure(tempBuffer, OSADLTemperatureData.GetType());
                            labelGPUTempArray[deviceIndex].Text = (OSADLTemperatureData.Temperature / 1000).ToString() + "℃";
                            labelGPUTempArray[deviceIndex].ForeColor = (OSADLTemperatureData.Temperature >= 80000) ? Color.Red :
                                                                       (OSADLTemperatureData.Temperature >= 60000) ? Color.Purple :
                                                                                                                     Color.Blue;
                        }
                    }

                    // activity
                    ADLPMActivity OSADLPMActivityData;
                    OSADLPMActivityData = new ADLPMActivity();
                    IntPtr activityBuffer = IntPtr.Zero;
                    size = Marshal.SizeOf(OSADLPMActivityData);
                    activityBuffer = Marshal.AllocCoTaskMem((int)size);
                    Marshal.StructureToPtr(OSADLPMActivityData, activityBuffer, false);

                    if (null != ADL.ADL_Overdrive5_CurrentActivity_Get)
                    {
                        int ADLRet = ADL.ADL_Overdrive5_CurrentActivity_Get(ADLAdapterIndexArray[deviceIndex], activityBuffer);
                        if (ADL.ADL_SUCCESS == ADLRet)
                        {
                            OSADLPMActivityData = (ADLPMActivity)Marshal.PtrToStructure(activityBuffer, OSADLPMActivityData.GetType());
                            labelGPUActivityArray[deviceIndex].Text = OSADLPMActivityData.iActivityPercent.ToString() + "%";
                            labelGPUCoreClockArray[deviceIndex].Text = (OSADLPMActivityData.iEngineClock / 100).ToString() + " MHz";
                            labelGPUMemoryClockArray[deviceIndex].Text = (OSADLPMActivityData.iMemoryClock / 100).ToString() + " MHz";
                        }
                    }

                    // fan speed
                    ADLFanSpeedValue OSADLFanSpeedValueData;
                    OSADLFanSpeedValueData = new ADLFanSpeedValue();
                    IntPtr fanSpeedValueBuffer = IntPtr.Zero;
                    size = Marshal.SizeOf(OSADLFanSpeedValueData);
                    OSADLFanSpeedValueData.iSpeedType = 1;
                    fanSpeedValueBuffer = Marshal.AllocCoTaskMem((int)size);
                    Marshal.StructureToPtr(OSADLFanSpeedValueData, fanSpeedValueBuffer, false);

                    if (null != ADL.ADL_Overdrive5_FanSpeed_Get)
                    {
                        int ADLRet = ADL.ADL_Overdrive5_FanSpeed_Get(ADLAdapterIndexArray[deviceIndex], 0, fanSpeedValueBuffer);
                        if (ADL.ADL_SUCCESS == ADLRet)
                        {
                            OSADLFanSpeedValueData = (ADLFanSpeedValue)Marshal.PtrToStructure(fanSpeedValueBuffer, OSADLFanSpeedValueData.GetType());
                            labelGPUFanArray[deviceIndex].Text = OSADLFanSpeedValueData.iFanSpeed.ToString() + "%";
                        }
                    }
                } else if (NVMLInitialized && device.Vendor.Equals("NVIDIA Corporation")) {
                    uint temp = 0;
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetTemperature(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlTemperatureSensors.Gpu, ref temp);
                    labelGPUTempArray[deviceIndex].Text      = temp.ToString() + "℃";
                    labelGPUTempArray[deviceIndex].ForeColor = (temp >= 80) ? Color.Red :
                                                               (temp >= 60) ? Color.Purple :
                                                                              Color.Blue;

                    uint fanSpeed = 0;
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetFanSpeed(nvmlDeviceArray[deviceIndex], ref fanSpeed);
                    labelGPUFanArray[deviceIndex].Text = fanSpeed.ToString() + "%";

                    ManagedCuda.Nvml.nvmlUtilization utilization = new ManagedCuda.Nvml.nvmlUtilization();
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetUtilizationRates(nvmlDeviceArray[deviceIndex], ref utilization);
                    labelGPUActivityArray[deviceIndex].Text = utilization.gpu.ToString() + "%";

                    uint clock = 0;
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetClockInfo(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlClockType.Graphics, ref clock);
                    labelGPUCoreClockArray[deviceIndex].Text = clock.ToString() + " MHz";
                    ManagedCuda.Nvml.NvmlNativeMethods.nvmlDeviceGetClockInfo(nvmlDeviceArray[deviceIndex], ManagedCuda.Nvml.nvmlClockType.Mem, ref clock);
                    labelGPUMemoryClockArray[deviceIndex].Text = clock.ToString() + " MHz";
                }
                ++deviceIndex;
            }
            DeviceManagementLibrariesMutex.ReleaseMutex();
        }

        private void DumpADLInfo()
        {
            int ADLRet = -1;
            int NumberOfAdapters = 0;
            int NumberOfDisplays = 0;

            if (null != ADL.ADL_Adapter_NumberOfAdapters_Get)
            {
                ADL.ADL_Adapter_NumberOfAdapters_Get(ref NumberOfAdapters);
            }

            // Get OS adpater info from ADL
            ADLAdapterInfoArray OSAdapterInfoData;
            OSAdapterInfoData = new ADLAdapterInfoArray();

            if (null != ADL.ADL_Adapter_AdapterInfo_Get)
            {
                IntPtr AdapterBuffer = IntPtr.Zero;
                int size = Marshal.SizeOf(OSAdapterInfoData);
                AdapterBuffer = Marshal.AllocCoTaskMem((int)size);
                Marshal.StructureToPtr(OSAdapterInfoData, AdapterBuffer, false);

                if (null != ADL.ADL_Adapter_AdapterInfo_Get)
                {
                    ADLRet = ADL.ADL_Adapter_AdapterInfo_Get(AdapterBuffer, size);
                    if (ADL.ADL_SUCCESS == ADLRet)
                    {
                        OSAdapterInfoData = (ADLAdapterInfoArray)Marshal.PtrToStructure(AdapterBuffer, OSAdapterInfoData.GetType());
                        int IsActive = 0;

                        for (int i = 0; i < NumberOfAdapters; i++)
                        {
                            // Check if the adapter is active
                            if (null != ADL.ADL_Adapter_Active_Get)
                                ADLRet = ADL.ADL_Adapter_Active_Get(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref IsActive);

                            if (ADL.ADL_SUCCESS == ADLRet)
                            {
                                Logger("Adapter is   : " + (0 == IsActive ? "DISABLED" : "ENABLED"));
                                Logger("Adapter Index: " + OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex.ToString());
                                Logger("Adapter UDID : " + OSAdapterInfoData.ADLAdapterInfo[i].UDID);
                                Logger("Bus No       : " + OSAdapterInfoData.ADLAdapterInfo[i].BusNumber.ToString());
                                Logger("Driver No    : " + OSAdapterInfoData.ADLAdapterInfo[i].DriverNumber.ToString());
                                Logger("Function No  : " + OSAdapterInfoData.ADLAdapterInfo[i].FunctionNumber.ToString());
                                Logger("Vendor ID    : " + OSAdapterInfoData.ADLAdapterInfo[i].VendorID.ToString());
                                Logger("Adapter Name : " + OSAdapterInfoData.ADLAdapterInfo[i].AdapterName);
                                Logger("Display Name : " + OSAdapterInfoData.ADLAdapterInfo[i].DisplayName);
                                Logger("Present      : " + (0 == OSAdapterInfoData.ADLAdapterInfo[i].Present ? "No" : "Yes"));
                                Logger("Exist        : " + (0 == OSAdapterInfoData.ADLAdapterInfo[i].Exist ? "No" : "Yes"));
                                Logger("Driver Path  : " + OSAdapterInfoData.ADLAdapterInfo[i].DriverPath);
                                Logger("Driver Path X: " + OSAdapterInfoData.ADLAdapterInfo[i].DriverPathExt);
                                Logger("PNP String   : " + OSAdapterInfoData.ADLAdapterInfo[i].PNPString);

                                // Obtain information about displays
                                ADLDisplayInfo oneDisplayInfo = new ADLDisplayInfo();

                                if (null != ADL.ADL_Display_DisplayInfo_Get)
                                {
                                    IntPtr DisplayBuffer = IntPtr.Zero;
                                    int j = 0;

                                    // Force the display detection and get the Display Info. Use 0 as last parameter to NOT force detection
                                    ADLRet = ADL.ADL_Display_DisplayInfo_Get(OSAdapterInfoData.ADLAdapterInfo[i].AdapterIndex, ref NumberOfDisplays, out DisplayBuffer, 1);
                                    if (ADL.ADL_SUCCESS == ADLRet)
                                    {
                                        List<ADLDisplayInfo> DisplayInfoData = new List<ADLDisplayInfo>();
                                        for (j = 0; j < NumberOfDisplays; j++)
                                        {
                                            oneDisplayInfo = (ADLDisplayInfo)Marshal.PtrToStructure(new IntPtr(DisplayBuffer.ToInt64() + j * Marshal.SizeOf(oneDisplayInfo)), oneDisplayInfo.GetType());
                                            DisplayInfoData.Add(oneDisplayInfo);
                                        }
                                        Logger("\nTotal Number of Displays supported: " + NumberOfDisplays.ToString());
                                        Logger("\nDispID  AdpID  Type OutType  CnctType Connected  Mapped  InfoValue DisplayName ");

                                        for (j = 0; j < NumberOfDisplays; j++)
                                        {
                                            int InfoValue = DisplayInfoData[j].DisplayInfoValue;
                                            string StrConnected = (1 == (InfoValue & 1)) ? "Yes" : "No ";
                                            string StrMapped = (2 == (InfoValue & 2)) ? "Yes" : "No ";
                                            int AdpID = DisplayInfoData[j].DisplayID.DisplayLogicalAdapterIndex;
                                            string StrAdpID = (AdpID < 0) ? "--" : AdpID.ToString("d2");

                                            Logger(DisplayInfoData[j].DisplayID.DisplayLogicalIndex.ToString() + "        " +
                                                                 StrAdpID + "      " +
                                                                 DisplayInfoData[j].DisplayType.ToString() + "      " +
                                                                 DisplayInfoData[j].DisplayOutputType.ToString() + "      " +
                                                                 DisplayInfoData[j].DisplayConnector.ToString() + "        " +
                                                                 StrConnected + "        " +
                                                                 StrMapped + "      " +
                                                                 InfoValue.ToString("x4") + "   " +
                                                                 DisplayInfoData[j].DisplayName.ToString());
                                        }
                                        Logger("");
                                    }
                                    else
                                    {
                                        Logger("ADL_Display_DisplayInfo_Get() returned error code " + ADLRet.ToString());
                                    }
                                    // Release the memory for the DisplayInfo structure
                                    if (IntPtr.Zero != DisplayBuffer)
                                        Marshal.FreeCoTaskMem(DisplayBuffer);
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger("ADL_Adapter_AdapterInfo_Get() returned error code " + ADLRet.ToString());
                    }
                }
                // Release the memory for the AdapterInfo structure
                if (IntPtr.Zero != AdapterBuffer)
                    Marshal.FreeCoTaskMem(AdapterBuffer);
            }
        }

        private void textBoxBitcoinAddress_TextChanged(object sender, EventArgs e)
        {
        }

        private void textBoxEthereumAddress_TextChanged(object sender, EventArgs e)
        {
        }

        private void textBoxMoneroAddress_TextChanged(object sender, EventArgs e)
        {
        }

        private void textBoxZcashAddress_TextChanged(object sender, EventArgs e)
        {
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            UpdateDatabase();
            UnloadPhyMemDriver();
            if (ADLInitialized && null != ADL.ADL_Main_Control_Destroy)
                ADL.ADL_Main_Control_Destroy();
        }

        private void timerDeviceStatusUpdates_Tick(object sender, EventArgs e)
        {
            UpdateDeviceStatus();
        }

        EthashStratum mStratum;
        List<Miner> mMiners = null;
        enum ApplicationGlobalState
        {
            Idle = 0,
            Mining = 1,
            Benchmarking = 2
        };
        ApplicationGlobalState appState = ApplicationGlobalState.Idle;

        public bool ValidateBitcoinAddress()
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$");
            var match = regex.Match(textBoxBitcoinAddress.Text);
            if (match.Success)
            {
                return true;
            }
            else
            {
                MessageBox.Show("Please enter a valid Bitcoin addresss.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ValidateEthereumAddress()
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("^0x[a-fA-Z0-9]{40}$");
            var match = regex.Match(textBoxEthereumAddress.Text);
            if (match.Success)
            {
                return true;
            }
            else
            {
                MessageBox.Show("Please enter a valid Ethereum addresss starting with \"0x\".", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        struct StratumServerInfo : IComparable<StratumServerInfo> {
            public String name; 
            public long delay;
            public long time;

            public StratumServerInfo(String aName, long aDelay)
            {
                name = aName;
                delay = aDelay;
                try
                {
                    time = Utilities.MeasurePingRoundtripTime(aName);
                }
                catch (Exception ex)
                {
                    time = -1;
                }
                if (time >= 0)
                    time += delay;
            }

            public int CompareTo(StratumServerInfo other)
            {
                if (time == other.time)
                {
                    return 0;
                }
                else if (other.time < 0 && time >= 0)
                {
                    return -1;
                }
                else if (other.time >= 0 && time < 0)
                {
                    return 1;
                }
                else if (other.time > time)
                {
                    return -1;
                }
                else
                {
                    return 1;
                }
            }
        };

        public void LaunchEthashMiners(String pool)
        {
            if (pool == "NiceHash" || mDevFee)
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("daggerhashimoto.usa.nicehash.com", 0),   
                                new StratumServerInfo("daggerhashimoto.eu.nicehash.com", 0),
                                new StratumServerInfo("daggerhashimoto.hk.nicehash.com", 150),
                                new StratumServerInfo("daggerhashimoto.jp.nicehash.com", 100),
                                new StratumServerInfo("daggerhashimoto.in.nicehash.com", 200),
                                new StratumServerInfo("daggerhashimoto.br.nicehash.com", 180)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new NiceHashEthashStratum(host.name, 3353, (mDevFee ? mDevFeeBitcoinAddress : textBoxBitcoinAddress.Text), "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else if (pool == "zawawa.net")
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("eth-uswest.zawawa.net", 0)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new OpenEthereumPoolEthashStratum(host.name, 4000, textBoxEthereumAddress.Text, "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else if (pool == "DwarfPool")
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("eth-eu.dwarfpool.com", 0),
                                new StratumServerInfo("eth-us.dwarfpool.com", 0),
                                new StratumServerInfo("eth-us2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ru.dwarfpool.com", 0),
                                new StratumServerInfo("eth-asia.dwarfpool.com", 0),
                                new StratumServerInfo("eth-cn.dwarfpool.com", 0),
                                new StratumServerInfo("eth-cn2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-sg.dwarfpool.com", 0),
                                new StratumServerInfo("eth-au.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ru2.dwarfpool.com", 0),
                                new StratumServerInfo("eth-hk.dwarfpool.com", 0),
                                new StratumServerInfo("eth-br.dwarfpool.com", 0),
                                new StratumServerInfo("eth-ar.dwarfpool.com", 0)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new OpenEthereumPoolEthashStratum(host.name, 8008, textBoxEthereumAddress.Text, "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else if (pool == "ethermine.org")
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("us1.ethermine.org", 0),
                                new StratumServerInfo("us2.ethermine.org", 0),
                                new StratumServerInfo("eu1.ethermine.org", 0),
                                new StratumServerInfo("asia1.ethermine.org", 0)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new OpenEthereumPoolEthashStratum(host.name, 4444, textBoxEthereumAddress.Text, "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else if (pool == "ethpool.org")
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("us1.ethpool.org", 0),
                                new StratumServerInfo("us2.ethpool.org", 0),
                                new StratumServerInfo("eu1.ethpool.org", 0),
                                new StratumServerInfo("asia1.ethpool.org", 0)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new OpenEthereumPoolEthashStratum(host.name, 3333, textBoxEthereumAddress.Text, "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else if (pool == "Nanopool")
            {
                var hosts = new List<StratumServerInfo> {
                                new StratumServerInfo("eth-eu1.nanopool.org", 0),
                                new StratumServerInfo("eth-eu2.nanopool.org", 0),
                                new StratumServerInfo("eth-asia1.nanopool.org", 0),
                                new StratumServerInfo("eth-us-east1.nanopool.org", 0),
                                new StratumServerInfo("eth-us-west1.nanopool.org", 0)
                            };
                hosts.Sort();
                foreach (StratumServerInfo host in hosts)
                {
                    if (host.time >= 0)
                    {
                        try
                        {
                            mStratum = new OpenEthereumPoolEthashStratum(host.name, 9999, textBoxEthereumAddress.Text, "x", pool);
                            break;
                        }
                        catch (Exception ex)
                        {
                            MainForm.Logger("Exception: " + ex.Message + ex.StackTrace);
                        }
                    }
                }
            }
            else
            {
                mStratum = new OpenEthereumPoolEthashStratum("eth-uswest.zawawa.net", 4000, textBoxEthereumAddress.Text, "x", pool);
            }
            mMiners = new List<Miner>();
            for (int deviceIndex = 0; deviceIndex < computeDeviceArray.Length; ++deviceIndex)
                mMiners.Add(new OpenCLEthashMiner(computeDeviceArray[deviceIndex], deviceIndex, mStratum));
        }

        private void LaunchMiners()
        {
            foreach (String pool in listBoxPoolPriorities.Items)
            {
                try
                {
                    Logger("Launching Ethash miners...");
                    LaunchEthashMiners(pool);
                    break;
                }
                catch (Exception ex)
                {
                    if (mStratum != null)
                        mStratum.Stop();
                    if (mMiners != null)
                        foreach (Miner miner in mMiners)
                            miner.Stop();
                    mStratum = null;
                    mMiners = null;
                }
            }
        }

        private void StopMiners()
        {
            Logger("Stopping miners...");
            foreach (Miner miner in mMiners)
                miner.Stop();
            mStratum.Stop();
            mMiners = null;
            mStratum = null;
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            UpdateDatabase();

            if (textBoxBitcoinAddress.Text != "" && !ValidateBitcoinAddress())
                return;
            if (textBoxEthereumAddress.Text != "" && !ValidateEthereumAddress())
                return;
            if (textBoxBitcoinAddress.Text == "" && textBoxEthereumAddress.Text == "")
            {
                MessageBox.Show("Please enter at least one valid wallet address.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.Enabled = false;

            if (appState == ApplicationGlobalState.Idle)
            {
                mStratum = null;
                mMiners = null;

                mDevFee = true;
                LaunchMiners();
                if (mStratum == null || mMiners == null)
                {
                    MessageBox.Show("Failed to launch miner.", appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    appState = ApplicationGlobalState.Mining;
                    tabControlMainForm.SelectedIndex = 0;
                    timerDevFee.Interval = mDevFeeDurationInSeconds * 1000;
                    timerDevFee.Enabled = true;
                }
            }
            else if (appState == ApplicationGlobalState.Mining) {
                timerDevFee.Enabled = false;
                try
                {
                    StopMiners();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to stop miner(s):\n" + ex.Message, appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    mStratum = null;
                    mMiners = null;
                }
                appState = ApplicationGlobalState.Idle;
            }

            UpdateDeviceStatus();
            UpdateCurrencyStats();
            UpdateControls();
        }

        private void UpdateControls()
        {
            buttonStart.Text = (appState == ApplicationGlobalState.Mining) ? "Stop" : "Start";
            buttonBenchmark.Enabled = false;

            groupBoxCoinsToMine.Enabled = (appState == ApplicationGlobalState.Idle);
            groupBoxPoolPriorities.Enabled = (appState == ApplicationGlobalState.Idle);
            textBoxBitcoinAddress.Enabled = (appState == ApplicationGlobalState.Idle);
            textBoxEthereumAddress.Enabled = (appState == ApplicationGlobalState.Idle);
            textBoxMoneroAddress.Enabled = (appState == ApplicationGlobalState.Idle);
            textBoxZcashAddress.Enabled = (appState == ApplicationGlobalState.Idle);

            this.Enabled = true;
        }

        private void timerCurrencyStatUpdates_Tick(object sender, EventArgs e)
        {
            UpdateCurrencyStats();
        }

        private void buttonPoolPrioritiesUp_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBoxPoolPriorities.SelectedIndex;
            if (selectedIndex > 0)
            {
                listBoxPoolPriorities.Items.Insert(selectedIndex - 1, listBoxPoolPriorities.Items[selectedIndex]);
                listBoxPoolPriorities.Items.RemoveAt(selectedIndex + 1);
                listBoxPoolPriorities.SelectedIndex = selectedIndex - 1;
                UpdateCurrencyStats();
            }
        }

        private void buttonPoolPrioritiesDown_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBoxPoolPriorities.SelectedIndex;
            if (selectedIndex < listBoxPoolPriorities.Items.Count - 1 & selectedIndex != -1)
            {
                listBoxPoolPriorities.Items.Insert(selectedIndex + 2, listBoxPoolPriorities.Items[selectedIndex]);
                listBoxPoolPriorities.Items.RemoveAt(selectedIndex);
                listBoxPoolPriorities.SelectedIndex = selectedIndex + 1;
                UpdateCurrencyStats();
            }
        }

        private void buttonViewBalancesAtNiceHash_Click(object sender, EventArgs e)
        {
            if (ValidateBitcoinAddress())
                System.Diagnostics.Process.Start("https://www.nicehash.com/miner/" + textBoxBitcoinAddress.Text);
        }

        private void tabControlMainForm_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDatabase();
        }

        private void buttonEthereumBalance_Click(object sender, EventArgs e)
        {
            if (!ValidateEthereumAddress())
                return;
            foreach (String poolName in listBoxPoolPriorities.Items) {
                if (poolName == "Nanopool")
                {
                    System.Diagnostics.Process.Start("https://eth.nanopool.org/account/" + textBoxEthereumAddress.Text);
                    return;
                }
                else if (poolName == "DwarfPool")
                {
                    System.Diagnostics.Process.Start("https://dwarfpool.com/eth/address?wallet=" + textBoxEthereumAddress.Text);
                    return;
                }
                else if (poolName == "ethermine.org")
                {
                    System.Diagnostics.Process.Start("https://ethermine.org/miners/" + textBoxEthereumAddress.Text);
                    return;
                }
                else if (poolName == "ethpool.org")
                {
                    System.Diagnostics.Process.Start("https://ethpool.org/miners/" + textBoxEthereumAddress.Text);
                    return;
                }
            }
        }

        private void timerDevFee_Tick(object sender, EventArgs e)
        {
            if (appState != ApplicationGlobalState.Mining)
            {
                timerDevFee.Enabled = false;
            }
            else if (mDevFee)
            {
                StopMiners();
                mDevFee = false;
                timerDevFee.Interval = (int)(((double)mDevFeeDurationInSeconds * ((double)(100 - mDevFeePercentage) / mDevFeePercentage)) * 1000);
                LaunchMiners();
            }
            else
            {
                StopMiners();
                mDevFee = true;
                timerDevFee.Interval = mDevFeeDurationInSeconds * 1000;
                LaunchMiners();
            }

            UpdateCurrencyStats();
        }
    }
}
