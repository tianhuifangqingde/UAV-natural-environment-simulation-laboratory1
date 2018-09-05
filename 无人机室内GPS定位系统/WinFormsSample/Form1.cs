using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;    //最后看一看有没有用到
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using NatNetML;    //这是一个封装函数 由运动捕捉厂商提供
using System.Linq;
//using System.Net;//以下是TCP/IP需要的数据集
using System.Net.Sockets;
//using System.IO;
//using System.Threading;



namespace WinFormTestApp
{
    public partial class Form1 : Form
    {
        // [NatNet] Our NatNet object
        private NatNetML.NatNetClientML m_NatNet;

        // [NatNet] Our NatNet Frame of Data object
        private NatNetML.FrameOfMocapData m_FrameOfData = new NatNetML.FrameOfMocapData();

        // [NatNet] Description of the Active Model List from the server (e.g. Motive)
        NatNetML.ServerDescription desc = new NatNetML.ServerDescription();

        // [NatNet] Queue holding our incoming mocap frames the NatNet server (e.g. Motive)
        private Queue<NatNetML.FrameOfMocapData> m_FrameQueue = new Queue<NatNetML.FrameOfMocapData>();

        // spreadsheet lookup
        Hashtable htMarkers = new Hashtable();
        Hashtable htRigidBodies = new Hashtable();
        List<RigidBody> mRigidBodies = new List<RigidBody>();
        Hashtable htSkelRBs = new Hashtable();

        Hashtable htForcePlates = new Hashtable();
        List<ForcePlate> mForcePlates = new List<ForcePlate>();

        // graphing support
        const int GraphFrames = 500;

        const int maxSeriesCount = 10;

        // frame timing information
        double m_fLastFrameTimestamp = 0.0f;
        float m_fCurrentMocapFrameTimestamp = 0.0f;
        float m_fFirstMocapFrameTimestamp = 0.0f;
        QueryPerfCounter m_FramePeriodTimer = new QueryPerfCounter();
        QueryPerfCounter m_UIUpdateTimer = new QueryPerfCounter();

        // server information
        double m_ServerFramerate = 1.0f;
        float m_ServerToMillimeters = 1.0f;
        int m_UpAxis = 1;   // 0=x, 1=y, 2=z (Y default)
        int mAnalogSamplesPerMocpaFrame = 0;
        int mDroppedFrames = 0;  //丢帧
        int mLastFrame = 0;

        private static object syncLock = new object();
        private delegate void OutputMessageCallback(string strMessage);
        private bool mPaused = false;

        // UI updating
        delegate void UpdateUICallback();
        bool mApplicationRunning = true;
        Thread UIUpdateThread;

        //定义一个队列 
        public Queue DATAQueue=new Queue();
       
        //UDP
        UdpClient udpClient;
        IPEndPoint ipEndPoint;

        bool mRecording = false;
        TextWriter mWriter;
        //定义发送和保存的数据
        public double WX, WY, WZ;  //相对位置
        public double WX_truth_R = 0.0, WY_truth_R = 0.0, WZ_truth_R = 0.0;  //相对位置
        public double WX_truth_Pre=0.0, WY_truth_Pre=0.0, WZ_truth_Pre=0.0;

        byte[] st = new byte[8];

        //有关TCP/IP 的定义       
        public delegate void showData(string msg);//委托,防止跨线程的访问控件，引起的安全异常
        private const int bufferSize = 176;//缓存空间
        private TcpClient client;
        private TcpClient client1;


        private TcpListener server;
        NetworkStream sendStream;
        NetworkStream sendStream1;
        static DATA IPDATABYTESEND = new DATA();

        struct IpAndPort
        {
            public string Ip;
            public string Port;
        }

        public static byte[] tmp = new byte[1760]; //GPS数据存储数组
        public struct DATA
        {
            public uint Syn_Flag ;//同步标记;
            public uint Packet_Length ;//信息体长度;
            public uint Command_ID ;//数据类型;
            public uint Send_Node ;//用户端编号;用作用户号 0:用户1,1:用户2;
            public uint Receive_Node ;//客户端编号;
            public uint Check_Code ;//验证码;
            public int wn;
            public int sow;
            public double ecef_pos_x;//位置
            public double ecef_pos_y;
            public double ecef_pos_z;
            public double ecef_vel_x ;//速度
            public double ecef_vel_y ;
            public double ecef_vel_z ;
            public double ecef_A_x ;//加速度;
            public double ecef_A_y ;
            public double ecef_A_z ;
            public double ecef_JA_x ;//加加速度;
            public double ecef_JA_y ;
            public double ecef_JA_z ;
            public double ULat;//纬度;
            public double ULon;//经度;
            public double UAlt;//高度;
            public double heading;//偏航角;
            public double elevation;//俯仰角;
            public double bank;//横滚角;
        }
  

         //XY坐标转GPS经纬度
         static double NAV_EQUATORIAL_RADIUS = (6378.137 * 1000.0);			    // meters
         static double NAV_FLATTENING = 1.0 / 298.257223563;			    // WGS-84
         static double M_PI = 3.14159265f;
         static double DEG_TO_RAD = M_PI / 180.0f;
         static double doubleNAV_E_2 = NAV_FLATTENING * (2.0 - NAV_FLATTENING);
         static double NAV_E_2 = NAV_FLATTENING * (2.0 - NAV_FLATTENING);
         static double sinLat2;
         static double r1, r2;
         static double local_Lat = 40.00000000, local_Lon = 116.00000000;//GPS局部坐标初始
         static double GPS_W_F, GPS_J_F,GPS_H_F;
         static double ECEF_X, ECEF_Y, ECEF_Z;


         //滑动均值数组
         public static double[] filtering_x = new double[5];
         public static int filtering_number_x = 0;
         public static double[] filtering_y = new double[5];
         public static int filtering_number_y = 0;
         public static double[] filtering_z = new double[5];
         public static int filtering_number_z = 0;


         public static double flt1 = 1;
         public static double flt2 = 0.5;

        public Form1()
        {
            InitializeComponent();
        }


        private void Form1_Load(object sender, EventArgs e)
        {
            Frame_head();  //初始化
            CalcEarthRadius(40);   //计算地球轨道半径 40是纬度
            String strMachineName = Dns.GetHostName(); //获得本地计算机的主机名
            IPHostEntry ipHost = Dns.GetHostByName(strMachineName);//将IP地址传给IP的容器
            foreach (IPAddress ip in ipHost.AddressList) //循环语句，如果获得本机IP将其赋值给comboxLocal
            {
                string strIP = ip.ToString();
                comboBoxLocal.Items.Add(strIP);
            }
            int selected = comboBoxLocal.Items.Add("127.0.0.1");//将工控机IP地址赋值给comboBoxlocal
            comboBoxLocal.SelectedItem = comboBoxLocal.Items[selected];

            // create NatNet client
            int iConnectionType = 0;         //定义链接的过度变量
            int iResult = CreateClient(iConnectionType);  //创建链接，返回结果

            // create and run an Update UI thread //创建一个更新的UI线程
            UpdateUICallback d = new UpdateUICallback(UpdateUI);
            UIUpdateThread = new Thread(() =>      //创建一个线程
            {
                while (mApplicationRunning)        //APP运行时不断进行UI更新
                {
                    try
                    {
                        this.Invoke(d);
                        Thread.Sleep(15);
                    }
                    catch (System.Exception ex)
                    {
                        OutputMessage(ex.Message);
                        break;
                    }
                }
            });
            UIUpdateThread.Start();              //UI更新线程开始
        
        }

        /// <summary>
        /// Create a new NatNet client, which manages all communication with the NatNet server (e.g. Motive)
        /// </summary>
        /// <param name="iConnectionType">0 = Multicast, 1 = Unicast</param>
        /// <returns></returns>
        /// 
        //创建客户端
        private int CreateClient(int iConnectionType)
        {
            // release any previous instance 释放之前所有的实例
            if (m_NatNet != null)
            {
                m_NatNet.Uninitialize();         //初始化
            }

            // [NatNet] create a new NatNet instance 创建一个新的实例
            m_NatNet = new NatNetML.NatNetClientML(iConnectionType);

            // [NatNet] set a "Frame Ready" callback function (event handler) handler that will be
            // called by NatNet when NatNet receives a frame of data from the server application
            //  [ natnet ]设置”框准备“回调函数（事件处理程序）处理，将叫NatNet时，NatNet收到一个从服务器应用程序的数据帧
            m_NatNet.OnFrameReady += new NatNetML.FrameReadyEventHandler(m_NatNet_OnFrameReady);

            /*
            // [NatNet] for testing only - event signature format required by some types of .NET applications (e.g. MatLab)
            m_NatNet.OnFrameReady2 += new FrameReadyEventHandler2(m_NatNet_OnFrameReady2);
            */

            // [NatNet] print version info  打印版本信息
            int[] ver = new int[4];
            ver = m_NatNet.NatNetVersion();
            String strVersion = String.Format("NatNet Version : {0}.{1}.{2}.{3}", ver[0], ver[1], ver[2], ver[3]);
            OutputMessage(strVersion);//弹出框输出版本号
            return 0;
        }

        /// <summary>
        /// Connect to a NatNet server (e.g. Motive)
        /// </summary>
        private void Connect()
        {
            // [NatNet] connect to a NatNet server
            int returnCode = 0;
            string strLocalIP = comboBoxLocal.SelectedItem.ToString();
            string strServerIP = textBoxServer.Text;
            returnCode = m_NatNet.Initialize(strLocalIP, strServerIP);
            if (returnCode == 0)
                OutputMessage("初始化成功");
            else
            {
                OutputMessage("初始化失败");
                checkBoxConnect.Checked = false;
            }

            // [NatNet] validate the connection
            returnCode = m_NatNet.GetServerDescription(desc);
            if (returnCode == 0)
            {
                OutputMessage("连接成功");
                OutputMessage("服务端ＡＰＰ名称: " + desc.HostApp);
                OutputMessage(String.Format("服务端ＡＰＰ版本: {0}.{1}.{2}.{3}", desc.HostAppVersion[0], desc.HostAppVersion[1], desc.HostAppVersion[2], desc.HostAppVersion[3]));
                OutputMessage(String.Format("服务端运动捕捉软件版本: {0}.{1}.{2}.{3}", desc.NatNetVersion[0], desc.NatNetVersion[1], desc.NatNetVersion[2], desc.NatNetVersion[3]));
                checkBoxConnect.Text = "断开连接";

                // Tracking Tools and Motive report in meters - lets convert to millimeters
                if (desc.HostApp.Contains("TrackingTools") || desc.HostApp.Contains("Motive"))
                    m_ServerToMillimeters = 1000.0f;

                // [NatNet] [optional] Query mocap server for the current camera framerate
                int nBytes = 0;
                byte[] response = new byte[10000];
                int rc;
                rc = m_NatNet.SendMessageAndWait("FrameRate", out response, out nBytes);
                if (rc == 0)
                {
                    try
                    {
                        m_ServerFramerate = BitConverter.ToSingle(response, 0);
                        OutputMessage(String.Format("摄像机帧速: {0}", m_ServerFramerate));
                    }
                    catch (System.Exception ex)
                    {
                        OutputMessage(ex.Message);
                    }
                }
                // [NatNet] [optional] Query mocap server for the current analog framerate
                rc = m_NatNet.SendMessageAndWait("AnalogSamplesPerMocapFrame", out response, out nBytes);
                if (rc == 0)
                {
                    try
                    {
                        mAnalogSamplesPerMocpaFrame = BitConverter.ToInt32(response, 0);
                    }
                    catch (System.Exception ex)
                    {
                        OutputMessage(ex.Message);
                    }
                }

                // [NatNet] [optional] Query mocap server for the current up axis
                rc = m_NatNet.SendMessageAndWait("UpAxis", out response, out nBytes);
                if (rc == 0)
                {
                    m_UpAxis = BitConverter.ToInt32(response, 0);
                }

                m_fCurrentMocapFrameTimestamp = 0.0f;
                m_fFirstMocapFrameTimestamp = 0.0f;
                mDroppedFrames = 0;
            }
            else
            {
                OutputMessage("连接失败");
                checkBoxConnect.Checked = false;
                checkBoxConnect.Text = "连接";
            }

        }

        private void Disconnect()
        {
            // [NatNet] disconnect
            // optional : for unicast clients only - notify Motive we are disconnecting
            int nBytes = 0;
            byte[] response = new byte[10000];
            int rc;
            rc = m_NatNet.SendMessageAndWait("Disconnect", out response, out nBytes);
            if (rc == 0)
            {

            }
            // shutdown our client socket
            m_NatNet.Uninitialize();
            checkBoxConnect.Text = "连接";
        }

        private void checkBoxConnect_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxConnect.Checked)
            {
                Connect();
            }
            else
            {
                Disconnect();
            }
        }


        //报警
        private void OutputMessage(string strMessage)
        {
            if (mPaused)
                return;

            if (!mApplicationRunning)
                return;

            if (this.listView1.InvokeRequired)
            {
                // It's on a different thread, so use Invoke 
                //这在另一个线程中，所以使用委托
                OutputMessageCallback d = new OutputMessageCallback(OutputMessage);
                this.Invoke(d, new object[] { strMessage });
            }
            else
            {
                // It's on the same thread, no need for Invoke
                //这在同一个线程，所以不需要委托
                DateTime d = DateTime.Now;
                String strTime = String.Format("{0}:{1}:{2}:{3}", d.Hour, d.Minute, d.Second, d.Millisecond);
                ListViewItem item = new ListViewItem(strTime, 0);
                item.SubItems.Add(strMessage);
                listView1.Items.Add(item);
            }
        }

        //发现刚体
        private RigidBody FindRB(int id, int parentID = -2)
        {
            foreach (RigidBody rb in mRigidBodies)
            {
                if (rb.ID == id)
                {
                    if (parentID != -2)
                    {
                        if (rb.parentID == parentID)
                            return rb;
                    }
                    else
                    {
                        return rb;
                    }
                }
            }
            return null;
        }


        /// <summary>
        /// Update the spreadsheet.  
        /// Note: This refresh is quite slow and provided here only as a complete example. 
        /// In a production setting this would be optimized.
        /// </summary>
        //更新数据 在这里可以对数据进行引用和处理

        private void UpdateDataGrid()
        {
            // update RigidBody data
            for (int i = 0; i < m_FrameOfData.nRigidBodies; i++)
            {
                NatNetML.RigidBodyData rb = m_FrameOfData.RigidBodies[i];
                int key = rb.ID.GetHashCode();

                // note : must add rb definitions here one time instead of on get data descriptions because we don't know the marker list yet.
                if (!htRigidBodies.ContainsKey(key))
                {
                    // Add RigidBody def to the grid
                    if ((rb.Markers[0] != null) && (rb.Markers[0].ID != -1))
                    {
                        string name;
                        RigidBody rbDef = FindRB(rb.ID);
                        if (rbDef != null)
                        {
                            name = rbDef.Name;
                        }
                        else
                        {
                            name = rb.ID.ToString();
                        }

                        int rowIndex = dataGridView1.Rows.Add("RigidBody: " + name);
                        key = rb.ID.GetHashCode();
                        htRigidBodies.Add(key, rowIndex);

                        // Add Markers associated with this rigid body to the grid
                        for (int j = 0; j < rb.nMarkers; j++)
                        {
                            String strUniqueName = name + "-" + rb.Markers[j].ID.ToString();
                            int keyMarker = strUniqueName.GetHashCode();
                            int newRowIndexMarker = dataGridView1.Rows.Add(strUniqueName);
                            htMarkers.Add(keyMarker, newRowIndexMarker);
                        }

                    }
                }
                else
                {
                    // update RigidBody data
                    int rowIndex = (int)htRigidBodies[key];
                    if (rowIndex >= 0)
                    {
                        bool tracked = rb.Tracked;
                        if (!tracked)
                        {
                            OutputMessage("RigidBody not tracked in this frame.");
                        }
                        //重点 这里是更新位置信息 而且Y和Z得对调一下
                        WX = Convert.ToDouble(rb.x * m_ServerToMillimeters / 1000);
                        WY = Convert.ToDouble(rb.z * m_ServerToMillimeters / 1000);
                        WZ = Convert.ToDouble(rb.y * m_ServerToMillimeters / 1000);
                        dataGridView1.Rows[rowIndex].Cells[1].Value = WX;
                        dataGridView1.Rows[rowIndex].Cells[2].Value = WY;
                        dataGridView1.Rows[rowIndex].Cells[3].Value = WZ;
                        //数据进入队列缓存
                        Queue(WX, WY, WZ);      
                        
                        // update Marker data associated with this rigid body
                        // 更新点的位置数据
                        for (int j = 0; j < rb.nMarkers; j++)
                        {
                            if (rb.Markers[j].ID != -1)
                            {
                                string name;
                                RigidBody rbDef = FindRB(rb.ID);
                                if (rbDef != null)
                                {
                                    name = rbDef.Name;
                                }
                                else
                                {
                                    name = rb.ID.ToString();
                                }

                                String strUniqueName = name + "-" + rb.Markers[j].ID.ToString();
                                int keyMarker = strUniqueName.GetHashCode();
                                if (htMarkers.ContainsKey(keyMarker))
                                {
                                    int rowIndexMarker = (int)htMarkers[keyMarker];
                                    NatNetML.Marker m = rb.Markers[j];
                                    dataGridView1.Rows[rowIndexMarker].Cells[1].Value = m.x;
                                    dataGridView1.Rows[rowIndexMarker].Cells[2].Value = m.z;
                                    dataGridView1.Rows[rowIndexMarker].Cells[3].Value = m.y;
                                }
                            }
                        }

                    }
                }
            }
        }

        /// <summary>
        /// [NatNet] Request a description of the Active Model List from the server (e.g. Motive) and build up a new spreadsheet  
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        //处理帧数据 //保存数据
        void ProcessFrameOfData(ref NatNetML.FrameOfMocapData data)
        {
            // detect and reported any 'reported' frame drop (as reported by server)
            if (m_fLastFrameTimestamp != 0.0f)
            {
                double framePeriod = 1.0f / m_ServerFramerate;
                double thisPeriod = data.fTimestamp - m_fLastFrameTimestamp;
                double fudgeFactor = 0.002f; // 2 ms
                if ((thisPeriod - framePeriod) > fudgeFactor)
                {
                    //OutputMessage("Frame Drop: ( ThisTS: " + data.fTimestamp.ToString("F3") + "  LastTS: " + m_fLastFrameTimestamp.ToString("F3") + " )");
                    mDroppedFrames++;
                }
            }

            // check and report frame drop (frame id based)
            if (mLastFrame != 0)
            {
                if ((data.iFrame - mLastFrame) != 1)
                {
                    //OutputMessage("Frame Drop: ( ThisFrame: " + data.iFrame.ToString() + "  LastFrame: " + mLastFrame.ToString() + " )");
                    //mDroppedFrames++;
                }
            }

            // recording : write packet to data file

      
            // [NatNet] Add the incoming frame of mocap data to our frame queue,  
            // Note: the frame queue is a shared resource with the UI thread, so lock it while writing
            lock (syncLock)
            {
                // [optional] clear the frame queue before adding a new frame
                m_FrameQueue.Clear();
                FrameOfMocapData deepCopy = new FrameOfMocapData(data);
                m_FrameQueue.Enqueue(deepCopy);
            }

            mLastFrame = data.iFrame;
            m_fLastFrameTimestamp = data.fTimestamp;

        }

        /// <summary>
        /// [NatNet] m_NatNet_OnFrameReady will be called when a frame of Mocap
        /// data has is received from the server application.
        ///
        /// Note: This callback is on the network service thread, so it is
        /// important to return from this function quickly as possible 
        /// to prevent incoming frames of data from buffering up on the
        /// network socket.
        ///
        /// Note: "data" is a reference structure to the current frame of data.
        /// NatNet re-uses this same instance for each incoming frame, so it should
        /// not be kept (the values contained in "data" will become replaced after
        /// this callback function has exited).
        /// </summary>
        /// <param name="data">The actual frame of mocap data</param>
        /// <param name="client">The NatNet client instance</param>
        void m_NatNet_OnFrameReady(NatNetML.FrameOfMocapData data, NatNetML.NatNetClientML client)
        {
            double elapsedIntraMS = 0.0f;
            QueryPerfCounter intraTimer = new QueryPerfCounter();
            intraTimer.Start();

            // detect and report and 'measured' frame drop (as measured by client)
            m_FramePeriodTimer.Stop();
            double elapsedMS = m_FramePeriodTimer.Duration();

            ProcessFrameOfData(ref data);

            // report if we are taking too long, which blocks packet receiving, which if long enough would result in socket buffer drop
            intraTimer.Stop();
            elapsedIntraMS = intraTimer.Duration();
            if (elapsedIntraMS > 5.0f)
            {
                OutputMessage("Warning : Frame handler taking too long: " + elapsedIntraMS.ToString("F2"));
            }
            m_FramePeriodTimer.Start();
        }



        //将文件保存在文本里
        private void WriteFrame(DATA temp)
        {
            String str = "";
            string st1 = "                              ";
            str = temp.wn.ToString() + st1 + temp.sow.ToString() + st1 + temp.ecef_pos_x.ToString("F6") + st1 + temp.ecef_pos_y.ToString("F6") + st1 + temp.ecef_pos_z.ToString("F6") + st1 +
                temp.ecef_vel_x.ToString("F6") + st1 + temp.ecef_vel_y.ToString("F6") + st1 + temp.ecef_vel_z.ToString("F6") + st1 + temp.ecef_A_x.ToString("F6") + st1 +
                temp.ecef_A_y.ToString("F6") + st1 + temp.ecef_A_z.ToString("F6") + st1 + temp.ecef_JA_x.ToString("F6") + st1 + temp.ecef_JA_y.ToString("F6") + st1 + temp.ecef_JA_z.ToString("F6") + st1 +
                temp.heading.ToString("F6") + st1 + temp.elevation.ToString("F6") + st1 + temp.bank.ToString("F6") + st1 + temp.ULon.ToString("F6") + st1 + temp.ULat.ToString("F6") + st1 +
                temp.UAlt.ToString("F6") + st1 + WX_truth_R.ToString("F6") + st1 + WY_truth_R.ToString("F6") + st1 + WZ_truth_R.ToString("F6");
            mWriter.WriteLine(str);
        }

        private void RecordDataButton_CheckedChanged(object sender, EventArgs e)
        {
            string st1 = "GPS周                             周内秒                                 ECEF_X                                       ECEF_Y                                      ECEF_Z                                      ECEF_VX                                ECEF_VY                                ECEF_VZ                              ECEF_AX                               ECEF_AY                               ECEF_AZ                               ECEF_AAX                              ECEF_AAY                              ECEF_AAZ                              ECEF_AAAX                             ECEF_AAAY                             ECEF_AAAZ                             LON                                     LAT                                    HEIGHT                                WX                                    WY                                    WZ";
            if (RecordDataButton.Checked)
            {
                try
                {
                    if (Save_name.Text == "")
                    {
                        mWriter = File.CreateText("数据存储.txt");
                        mWriter.WriteLine(st1);
                    }
                    else
                    {
                        mWriter = File.CreateText(Save_name.Text + ".txt");
                        mWriter.WriteLine(st1);
                    }
                    mRecording = true;
                    RecordDataButton.Text = "正在保存数据";
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("出错");
                }
            }
            else
            {
                mWriter.Close();
                mRecording = false;
                RecordDataButton.Text = "保存数据";
            }
        }
        //UI更新线程
        private void UpdateUI()
        {
            m_UIUpdateTimer.Stop();            //UI数据时间更新停止
            double interframeDuration = m_UIUpdateTimer.Duration();//定义持续时间

            QueryPerfCounter uiIntraFrameTimer = new QueryPerfCounter();
            uiIntraFrameTimer.Start();         //UI数据时间更新开始

            // the frame queue is a shared resource with the FrameOfMocap delivery thread, so lock it while reading
            // note this can block the frame delivery thread.  In a production application frame queue management would be optimized.
            //帧队列是一个共享资源的frameofmocap输送线，所以把它锁在阅读
            //注意，这可以阻止帧传递线程。在生产应用程序框架中，队列管理将得到优化。
            //当我们使用线程的时候，效率最高的方式当然是异步，即各个线程同时运行，其间不相互依赖和等待。
            //但当不同的线程都需要访问某个资源的时候，就需要同步机制了，也就是说当对同一个资源进行读写的时候，我们要使该资源在同一时刻只能被一个线程操作，以确保每个操作都是有效即时的，也即保证其操作的原子性。lock是C#中最常用的同步方式
            lock (syncLock)
            {
                while (m_FrameQueue.Count > 0)   //如果帧队列数据个数大于0
                {
                    m_FrameOfData = m_FrameQueue.Dequeue(); //帧队列赋值

                    if (m_FrameQueue.Count > 0)
                        continue;

                    if (m_FrameOfData != null)
                    {
                        // for servers that only use timestamps, not frame numbers, calculate a 
                        // frame number from the time delta between frames
                        if (desc.HostApp.Contains("TrackingTools"))
                        {
                            m_fCurrentMocapFrameTimestamp = m_FrameOfData.fLatency;
                            if (m_fCurrentMocapFrameTimestamp == m_fLastFrameTimestamp)
                            {
                                continue;
                            }
                            if (m_fFirstMocapFrameTimestamp == 0.0f)
                            {
                                m_fFirstMocapFrameTimestamp = m_fCurrentMocapFrameTimestamp;
                            }
                            m_FrameOfData.iFrame = (int)((m_fCurrentMocapFrameTimestamp - m_fFirstMocapFrameTimestamp) * m_ServerFramerate);

                        }

                        // update the data grid
                        UpdateDataGrid();  //更新数据
                        // Mocap server timestamp (in seconds)
                        //m_fLastFrameTimestamp = m_FrameOfData.fTimestamp;
                        TimestampValue.Text = m_FrameOfData.fTimestamp.ToString("F3");//帧数据中的时间赋值给TimestampValue
                        DroppedFrameCountLabel.Text = mDroppedFrames.ToString();  //丢帧
                    }
                }
            }

            uiIntraFrameTimer.Stop();  //UI帧数据时间停止
            double uiIntraFrameDuration = uiIntraFrameTimer.Duration(); //持续时间赋值
            m_UIUpdateTimer.Start();  //UI更新时间计数

        }
        double RadiansToDegrees(double dRads)
        {
            return dRads * (180.0f / Math.PI);
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            mApplicationRunning = false;

            if (UIUpdateThread.IsAlive)
                UIUpdateThread.Abort();

            m_NatNet.Uninitialize();
            Application.Exit();
           
        }



        //TCP/IP连接  
        private void btnConnect_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(reciveAndListener);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            if (btnConnect.Text == "侦听")
            {
                if (txtIP.Text.Trim() == string.Empty)
                {
                    return;
                }
                if (txtPort.Text.Trim() == string.Empty)
                {
                    return;
                }
                //如果线程绑定的方法带有参数的话，那么这个参数的类型必须是object类型，所以讲ip,和端口号 写成一个结构体进行传递
                IpAndPort ipHePort = new IpAndPort();
                ipHePort.Ip = txtIP.Text;
                ipHePort.Port = txtPort.Text;
                thread.Start((object)ipHePort);
                btnConnect.Text = "关闭";
            }
            else
            {
                thread.Abort();
                server.Stop();
                btnConnect.Text = "侦听";
                OutputMessage("服务端关闭");
            }
        }

        private void reciveAndListener(object ipAndPort)
        {  
            IpAndPort ipHePort = (IpAndPort)ipAndPort;
            IPAddress ip = IPAddress.Parse(ipHePort.Ip);
            server = new TcpListener(ip, int.Parse(ipHePort.Port));
            server.Start();//启动监听
            OutputMessage("服务端开启侦听....");
            //  btnStart.IsEnabled = false;

            //获取连接的客户端对象 2个
            client = server.AcceptTcpClient();
            OutputMessage("有客户端1请求连接，连接已建立！");
            client1 = server.AcceptTcpClient();
            OutputMessage("有客户端2请求连接，连接已建立！");
            //获得流
            NetworkStream reciveStream = client.GetStream();
            NetworkStream reciveStream1 = client1.GetStream();

            TimeSpan ts;
            ts = System.DateTime.Now.Subtract(DateTime.Parse("1980-1-6"));
            IPDATABYTESEND.wn = 1965;
            IPDATABYTESEND.sow = 345600000+190; 
         
            #region 循环监听客户端发来的信息
            do
            {
                byte[] buffer = new byte[bufferSize];
                int msgSize;
                try
                {                 
                    lock (reciveStream)
                    {
                        msgSize = reciveStream.Read(buffer, 0, bufferSize);
                    }
                    if (msgSize == 0)
                    return;
                    Formatting_Data();                
                }
                catch
                {
                    OutputMessage("出现异常：连接被迫关闭");
                    break;
                }
            } while (btnConnect.Text == "关闭");

            #endregion
        }




        void Formatting_Data()  //数据打包
        {
            int number = 0;
               for(int i=0;i<10;i++)
               {
                   byte[] buffer = new byte[176];
                   if (IPDATABYTESEND.sow > 604800000)  //如果跨周
                   {
                       IPDATABYTESEND.wn = IPDATABYTESEND.wn + 1;
                       IPDATABYTESEND.sow = IPDATABYTESEND.sow - 604800000;
                   }
                   //IPDATABYTESEND.wn = IPDATABYTESEND.wn+1;
                   IPDATABYTESEND.sow = IPDATABYTESEND.sow + 10;
                   number = (DATAQueue.Count / 3);
                   if (number > 0)
                   {
                     WX_truth_R = (double)DATAQueue.Dequeue();
                     WY_truth_R = (double)DATAQueue.Dequeue();
                     WZ_truth_R = (double)DATAQueue.Dequeue();
                   }
                   CalcGlobalLocation(WX_truth_R, WY_truth_R, WZ_truth_R); //计算位移后的经纬度
                   IPDATABYTESEND.UAlt = GPS_H_F;
                   IPDATABYTESEND.ULat = GPS_W_F;
                   IPDATABYTESEND.ULon = GPS_J_F;
                   GPStoECEF(GPS_J_F, GPS_W_F, GPS_H_F);        //经纬度转ECEF坐标
                   IPDATABYTESEND.ecef_pos_x = flt1 * ECEF_X + (1 - flt1) * IPDATABYTESEND.ecef_pos_x;  //X
                   IPDATABYTESEND.ecef_pos_y = flt1 * ECEF_Y + (1 - flt1) * IPDATABYTESEND.ecef_pos_y;  //Y
                   IPDATABYTESEND.ecef_pos_z = flt1 * ECEF_Z + (1 - flt1) * IPDATABYTESEND.ecef_pos_z;  //Z
                   //ECEF方向的速度计算
                   if (number > 0)
                   {
                   IPDATABYTESEND.ecef_vel_x = Sliding_filtering_x((IPDATABYTESEND.ecef_pos_x - WX_truth_Pre) / 0.01);
                   IPDATABYTESEND.ecef_vel_y = Sliding_filtering_y((IPDATABYTESEND.ecef_pos_y - WY_truth_Pre) / 0.01);
                   IPDATABYTESEND.ecef_vel_z = Sliding_filtering_z((IPDATABYTESEND.ecef_pos_z - WZ_truth_Pre) / 0.01);
                   }
                   else
                   {
                       IPDATABYTESEND.ecef_vel_x = Sliding_filtering_x(IPDATABYTESEND.ecef_vel_x);
                       IPDATABYTESEND.ecef_vel_y = Sliding_filtering_y(IPDATABYTESEND.ecef_vel_y);
                       IPDATABYTESEND.ecef_vel_z = Sliding_filtering_z(IPDATABYTESEND.ecef_vel_z);
                   }
                   if (IPDATABYTESEND.ecef_vel_x > 10) { IPDATABYTESEND.ecef_vel_x = 10; }
                   if (IPDATABYTESEND.ecef_vel_y > 10) { IPDATABYTESEND.ecef_vel_y = 10; }
                   if (IPDATABYTESEND.ecef_vel_z > 10) { IPDATABYTESEND.ecef_vel_z = 10; }
                   //将数据存入发送缓存区
                   buffer = StructToBytes(IPDATABYTESEND);
                   System.Buffer.BlockCopy(buffer, 0, tmp, buffer.Length * i, buffer.Length);
                   //数据保存
                   if (mRecording)
                   {
                       WriteFrame(IPDATABYTESEND);
                   }
                   WX_truth_Pre = IPDATABYTESEND.ecef_pos_x;
                   WY_truth_Pre = IPDATABYTESEND.ecef_pos_y;
                   WZ_truth_Pre = IPDATABYTESEND.ecef_pos_z;
               }
        
           theout();
        }
 
          void Frame_head()  //发送给GPS的帧头
        {
                IPDATABYTESEND.Syn_Flag = 0xFFFF8080;
                IPDATABYTESEND.Packet_Length = 176; //结构体的长度
                IPDATABYTESEND.Command_ID = 0x10;
                IPDATABYTESEND.Send_Node = 0x0;
                IPDATABYTESEND.Receive_Node = 0x0;
                IPDATABYTESEND.Check_Code = 0xAAAA5555;
                IPDATABYTESEND.heading = 0.000000;     //朝向;          
                IPDATABYTESEND.elevation = 0.000000;   //倾角;
                IPDATABYTESEND.bank = 0.000000;  		 //滚转角;  
                IPDATABYTESEND.ecef_vel_x = 0.0;
                IPDATABYTESEND.ecef_vel_y = 0.0;
                IPDATABYTESEND.ecef_vel_z = 0.0;
                IPDATABYTESEND.ecef_A_x = 0.000000;
                IPDATABYTESEND.ecef_A_y = 0.000000;
                IPDATABYTESEND.ecef_A_z = 0.000000;
                IPDATABYTESEND.ecef_JA_x = 0.000000;
                IPDATABYTESEND.ecef_JA_y = 0.000000;
                IPDATABYTESEND.ecef_JA_z = 0.000000;
                CalcGlobalLocation(0, 0, 0); //计算位移后的经纬度
                IPDATABYTESEND.UAlt = GPS_H_F;
                IPDATABYTESEND.ULat = GPS_W_F;
                IPDATABYTESEND.ULon = GPS_J_F;
                GPStoECEF(GPS_J_F, GPS_W_F, GPS_H_F);        //经纬度转ECEF坐标
                IPDATABYTESEND.ecef_pos_x = ECEF_X;  //X
                IPDATABYTESEND.ecef_pos_y = ECEF_Y;  //Y
                IPDATABYTESEND.ecef_pos_z = ECEF_Z;  //Z
                WX_truth_Pre = IPDATABYTESEND.ecef_pos_x;
                WY_truth_Pre = IPDATABYTESEND.ecef_pos_y;
                WZ_truth_Pre = IPDATABYTESEND.ecef_pos_z;
        }

        public void theout()
        {  
            sendStream = client.GetStream();//获得用于数据传输的流
            sendStream1 = client1.GetStream();//获得用于数据传输的流
            byte[] buffer = tmp;//将数据存进缓存中       
            sendStream.Write(buffer, 0, 1760);//最终写入流中
            sendStream1.Write(buffer, 0, 1760);//最终写入流中           
            Array.Clear(tmp, 0, tmp.Length);//清空发送缓存数组 
        }

        //XY坐标转GPS经纬度
        void CalcEarthRadius(double lat)
        {
            sinLat2 = Math.Sin(lat * (double)DEG_TO_RAD);
            sinLat2 = sinLat2 * sinLat2;
            r1 = (double)NAV_EQUATORIAL_RADIUS * (double)DEG_TO_RAD * ((double)1.0 - (double)NAV_E_2) / Math.Pow((double)1.0 - ((double)NAV_E_2 * sinLat2), ((double)3.0 / (double)2.0));
            r2 = (double)NAV_EQUATORIAL_RADIUS * (double)DEG_TO_RAD / Math.Sqrt((double)1.0 - ((double)NAV_E_2 * sinLat2)) * Math.Cos(lat * (double)DEG_TO_RAD);
        }

        void CalcGlobalLocation(double posNorth, double posEast,double height)
        {
            GPS_W_F = posNorth / (double)(r1 + 0.1) + local_Lat;
            GPS_J_F = posEast / (double)(r2 + 0.1) + local_Lon;
            GPS_H_F = height;
        }
        //经纬高转ECEF
        void GPStoECEF(double lon,double lat,double height)
        {
            lon = lon / 180 * Math.PI;
            lat = lat / 180 * Math.PI;
            double setaparametera = 6378137.0;
            double setaparametere2 = NAV_E_2;
            double radarrn = setaparametera / Math.Sqrt(1 - setaparametere2 * Math.Sin(lat) * Math.Sin(lat));
            ECEF_X = (radarrn + height) * Math.Cos(lat) * Math.Cos(lon);
            ECEF_Y = (radarrn + height) * Math.Cos(lat) * Math.Sin(lon);
            ECEF_Z = (radarrn * (1 - setaparametere2) + height) * Math.Sin(lat);


            //lon = lon / 180 * Math.PI;
            //lat = lat / 180 * Math.PI;    
            //double a = 6378137.0;
            //double b = 6356752.314;
            //double radarrn = a * a / Math.Sqrt(a * a * Math.Cos(lat) * Math.Cos(lat) + b * b * Math.Sin(lat) * Math.Sin(lat));
            //ECEF_X = (radarrn + height) * Math.Cos(lat) * Math.Cos(lon);
            //ECEF_Y = (radarrn + height) * Math.Cos(lat) * Math.Sin(lon);
            //ECEF_Z = (b * b / (a * a) * radarrn + height) * Math.Sin(lat);
        }

        //定义一个类 查询计数    KERNEL32.DLL 封装函数调用
        public class QueryPerfCounter
        {
            [DllImport("KERNEL32")]
            private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

            [DllImport("Kernel32.dll")]
            private static extern bool QueryPerformanceFrequency(out long lpFrequency);
            private long start;
            private long stop;
            private long frequency;
            Decimal multiplier = new Decimal(1.0e9);

            public QueryPerfCounter()
            {
                if (QueryPerformanceFrequency(out frequency) == false)
                {
                    // Frequency not supported
                    throw new Win32Exception();
                }
            }

            public void Start()
            {
                QueryPerformanceCounter(out start);
            }

            public void Stop()
            {
                QueryPerformanceCounter(out stop);
            }

            // return elapsed time between start and stop, in milliseconds.
            public double Duration()
            {
                double val = ((double)(stop - start) * (double)multiplier) / (double)frequency;
                val = val / 1000000.0f;   // convert to ms
                return val;
            }
        }


        public static byte[] StructToBytes(object structObj)
        {
            int size = Marshal.SizeOf(structObj);
            byte[] bytes = new byte[size];
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(structObj, structPtr, false);
            Marshal.Copy(structPtr, bytes, 0, size);
            Marshal.FreeHGlobal(structPtr);
            return bytes;
        }

        void Queue(double temp1, double temp2, double temp3)
        {
            if (DATAQueue.Count < 33)  //缓存10包
            {
                DATAQueue.Enqueue(temp1);
                DATAQueue.Enqueue(temp2);
                DATAQueue.Enqueue(temp3);
            }
            else
            {
                DATAQueue.Dequeue();
                DATAQueue.Dequeue();
                DATAQueue.Dequeue();
                DATAQueue.Enqueue(temp1);
                DATAQueue.Enqueue(temp2);
                DATAQueue.Enqueue(temp3);
            }
        }

        double Sliding_filtering_x(double data)
        {
            double sum = 0;
            filtering_x[filtering_number_x++] = data;
            if (filtering_number_x == 5)
            {
                for (int i = 0; i < 5; i++)
                {
                    sum += filtering_x[i];
                }
                filtering_number_x = filtering_number_x - 1;
                for (int b = 0; b < 4; b++)
                {
                    filtering_x[b] = filtering_x[b + 1];
                }
                return sum / 5;
            }
            else
            {
                return data;
            }
        }
        double Sliding_filtering_y(double data)
        {
            double sum = 0;
            filtering_y[filtering_number_y++] = data;
            if (filtering_number_y == 5)
            {
                for (int i = 0; i < 5; i++)
                {
                    sum += filtering_y[i];
                }
                filtering_number_y = filtering_number_y - 1;
                for (int b = 0; b < 4; b++)
                {
                    filtering_y[b] = filtering_y[b + 1];
                }
                return sum / 5;
            }
            else
            {
                return data;
            }
        }
        double Sliding_filtering_z(double data)
        {
            double sum = 0;
            filtering_z[filtering_number_z++] = data;
            if (filtering_number_z == 5)
            {
                for (int i = 0; i < 5; i++)
                {
                    sum += filtering_z[i];
                }
                filtering_number_z = filtering_number_z - 1;
                for (int b = 0; b < 4; b++)
                {
                    filtering_z[b] = filtering_z[b + 1];
                }
                return sum / 5;
            }
            else
            {
                return data;
            }
        }
    }
}