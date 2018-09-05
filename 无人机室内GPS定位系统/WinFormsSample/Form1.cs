using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;    //���һ����û���õ�
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using NatNetML;    //����һ����װ���� ���˶���׽�����ṩ
using System.Linq;
//using System.Net;//������TCP/IP��Ҫ�����ݼ�
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
        int mDroppedFrames = 0;  //��֡
        int mLastFrame = 0;

        private static object syncLock = new object();
        private delegate void OutputMessageCallback(string strMessage);
        private bool mPaused = false;

        // UI updating
        delegate void UpdateUICallback();
        bool mApplicationRunning = true;
        Thread UIUpdateThread;

        //����һ������ 
        public Queue DATAQueue=new Queue();
       
        //UDP
        UdpClient udpClient;
        IPEndPoint ipEndPoint;

        bool mRecording = false;
        TextWriter mWriter;
        //���巢�ͺͱ��������
        public double WX, WY, WZ;  //���λ��
        public double WX_truth_R = 0.0, WY_truth_R = 0.0, WZ_truth_R = 0.0;  //���λ��
        public double WX_truth_Pre=0.0, WY_truth_Pre=0.0, WZ_truth_Pre=0.0;

        byte[] st = new byte[8];

        //�й�TCP/IP �Ķ���       
        public delegate void showData(string msg);//ί��,��ֹ���̵߳ķ��ʿؼ�������İ�ȫ�쳣
        private const int bufferSize = 176;//����ռ�
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

        public static byte[] tmp = new byte[1760]; //GPS���ݴ洢����
        public struct DATA
        {
            public uint Syn_Flag ;//ͬ�����;
            public uint Packet_Length ;//��Ϣ�峤��;
            public uint Command_ID ;//��������;
            public uint Send_Node ;//�û��˱��;�����û��� 0:�û�1,1:�û�2;
            public uint Receive_Node ;//�ͻ��˱��;
            public uint Check_Code ;//��֤��;
            public int wn;
            public int sow;
            public double ecef_pos_x;//λ��
            public double ecef_pos_y;
            public double ecef_pos_z;
            public double ecef_vel_x ;//�ٶ�
            public double ecef_vel_y ;
            public double ecef_vel_z ;
            public double ecef_A_x ;//���ٶ�;
            public double ecef_A_y ;
            public double ecef_A_z ;
            public double ecef_JA_x ;//�Ӽ��ٶ�;
            public double ecef_JA_y ;
            public double ecef_JA_z ;
            public double ULat;//γ��;
            public double ULon;//����;
            public double UAlt;//�߶�;
            public double heading;//ƫ����;
            public double elevation;//������;
            public double bank;//�����;
        }
  

         //XY����תGPS��γ��
         static double NAV_EQUATORIAL_RADIUS = (6378.137 * 1000.0);			    // meters
         static double NAV_FLATTENING = 1.0 / 298.257223563;			    // WGS-84
         static double M_PI = 3.14159265f;
         static double DEG_TO_RAD = M_PI / 180.0f;
         static double doubleNAV_E_2 = NAV_FLATTENING * (2.0 - NAV_FLATTENING);
         static double NAV_E_2 = NAV_FLATTENING * (2.0 - NAV_FLATTENING);
         static double sinLat2;
         static double r1, r2;
         static double local_Lat = 40.00000000, local_Lon = 116.00000000;//GPS�ֲ������ʼ
         static double GPS_W_F, GPS_J_F,GPS_H_F;
         static double ECEF_X, ECEF_Y, ECEF_Z;


         //������ֵ����
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
            Frame_head();  //��ʼ��
            CalcEarthRadius(40);   //����������뾶 40��γ��
            String strMachineName = Dns.GetHostName(); //��ñ��ؼ������������
            IPHostEntry ipHost = Dns.GetHostByName(strMachineName);//��IP��ַ����IP������
            foreach (IPAddress ip in ipHost.AddressList) //ѭ����䣬�����ñ���IP���丳ֵ��comboxLocal
            {
                string strIP = ip.ToString();
                comboBoxLocal.Items.Add(strIP);
            }
            int selected = comboBoxLocal.Items.Add("127.0.0.1");//�����ػ�IP��ַ��ֵ��comboBoxlocal
            comboBoxLocal.SelectedItem = comboBoxLocal.Items[selected];

            // create NatNet client
            int iConnectionType = 0;         //�������ӵĹ��ȱ���
            int iResult = CreateClient(iConnectionType);  //�������ӣ����ؽ��

            // create and run an Update UI thread //����һ�����µ�UI�߳�
            UpdateUICallback d = new UpdateUICallback(UpdateUI);
            UIUpdateThread = new Thread(() =>      //����һ���߳�
            {
                while (mApplicationRunning)        //APP����ʱ���Ͻ���UI����
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
            UIUpdateThread.Start();              //UI�����߳̿�ʼ
        
        }

        /// <summary>
        /// Create a new NatNet client, which manages all communication with the NatNet server (e.g. Motive)
        /// </summary>
        /// <param name="iConnectionType">0 = Multicast, 1 = Unicast</param>
        /// <returns></returns>
        /// 
        //�����ͻ���
        private int CreateClient(int iConnectionType)
        {
            // release any previous instance �ͷ�֮ǰ���е�ʵ��
            if (m_NatNet != null)
            {
                m_NatNet.Uninitialize();         //��ʼ��
            }

            // [NatNet] create a new NatNet instance ����һ���µ�ʵ��
            m_NatNet = new NatNetML.NatNetClientML(iConnectionType);

            // [NatNet] set a "Frame Ready" callback function (event handler) handler that will be
            // called by NatNet when NatNet receives a frame of data from the server application
            //  [ natnet ]���á���׼�����ص��������¼�������򣩴�������NatNetʱ��NatNet�յ�һ���ӷ�����Ӧ�ó��������֡
            m_NatNet.OnFrameReady += new NatNetML.FrameReadyEventHandler(m_NatNet_OnFrameReady);

            /*
            // [NatNet] for testing only - event signature format required by some types of .NET applications (e.g. MatLab)
            m_NatNet.OnFrameReady2 += new FrameReadyEventHandler2(m_NatNet_OnFrameReady2);
            */

            // [NatNet] print version info  ��ӡ�汾��Ϣ
            int[] ver = new int[4];
            ver = m_NatNet.NatNetVersion();
            String strVersion = String.Format("NatNet Version : {0}.{1}.{2}.{3}", ver[0], ver[1], ver[2], ver[3]);
            OutputMessage(strVersion);//����������汾��
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
                OutputMessage("��ʼ���ɹ�");
            else
            {
                OutputMessage("��ʼ��ʧ��");
                checkBoxConnect.Checked = false;
            }

            // [NatNet] validate the connection
            returnCode = m_NatNet.GetServerDescription(desc);
            if (returnCode == 0)
            {
                OutputMessage("���ӳɹ�");
                OutputMessage("����ˣ��У�����: " + desc.HostApp);
                OutputMessage(String.Format("����ˣ��Уа汾: {0}.{1}.{2}.{3}", desc.HostAppVersion[0], desc.HostAppVersion[1], desc.HostAppVersion[2], desc.HostAppVersion[3]));
                OutputMessage(String.Format("������˶���׽����汾: {0}.{1}.{2}.{3}", desc.NatNetVersion[0], desc.NatNetVersion[1], desc.NatNetVersion[2], desc.NatNetVersion[3]));
                checkBoxConnect.Text = "�Ͽ�����";

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
                        OutputMessage(String.Format("�����֡��: {0}", m_ServerFramerate));
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
                OutputMessage("����ʧ��");
                checkBoxConnect.Checked = false;
                checkBoxConnect.Text = "����";
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
            checkBoxConnect.Text = "����";
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


        //����
        private void OutputMessage(string strMessage)
        {
            if (mPaused)
                return;

            if (!mApplicationRunning)
                return;

            if (this.listView1.InvokeRequired)
            {
                // It's on a different thread, so use Invoke 
                //������һ���߳��У�����ʹ��ί��
                OutputMessageCallback d = new OutputMessageCallback(OutputMessage);
                this.Invoke(d, new object[] { strMessage });
            }
            else
            {
                // It's on the same thread, no need for Invoke
                //����ͬһ���̣߳����Բ���Ҫί��
                DateTime d = DateTime.Now;
                String strTime = String.Format("{0}:{1}:{2}:{3}", d.Hour, d.Minute, d.Second, d.Millisecond);
                ListViewItem item = new ListViewItem(strTime, 0);
                item.SubItems.Add(strMessage);
                listView1.Items.Add(item);
            }
        }

        //���ָ���
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
        //�������� ��������Զ����ݽ������úʹ���

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
                        //�ص� �����Ǹ���λ����Ϣ ����Y��Z�öԵ�һ��
                        WX = Convert.ToDouble(rb.x * m_ServerToMillimeters / 1000);
                        WY = Convert.ToDouble(rb.z * m_ServerToMillimeters / 1000);
                        WZ = Convert.ToDouble(rb.y * m_ServerToMillimeters / 1000);
                        dataGridView1.Rows[rowIndex].Cells[1].Value = WX;
                        dataGridView1.Rows[rowIndex].Cells[2].Value = WY;
                        dataGridView1.Rows[rowIndex].Cells[3].Value = WZ;
                        //���ݽ�����л���
                        Queue(WX, WY, WZ);      
                        
                        // update Marker data associated with this rigid body
                        // ���µ��λ������
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

        //����֡���� //��������
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



        //���ļ��������ı���
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
            string st1 = "GPS��                             ������                                 ECEF_X                                       ECEF_Y                                      ECEF_Z                                      ECEF_VX                                ECEF_VY                                ECEF_VZ                              ECEF_AX                               ECEF_AY                               ECEF_AZ                               ECEF_AAX                              ECEF_AAY                              ECEF_AAZ                              ECEF_AAAX                             ECEF_AAAY                             ECEF_AAAZ                             LON                                     LAT                                    HEIGHT                                WX                                    WY                                    WZ";
            if (RecordDataButton.Checked)
            {
                try
                {
                    if (Save_name.Text == "")
                    {
                        mWriter = File.CreateText("���ݴ洢.txt");
                        mWriter.WriteLine(st1);
                    }
                    else
                    {
                        mWriter = File.CreateText(Save_name.Text + ".txt");
                        mWriter.WriteLine(st1);
                    }
                    mRecording = true;
                    RecordDataButton.Text = "���ڱ�������";
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("����");
                }
            }
            else
            {
                mWriter.Close();
                mRecording = false;
                RecordDataButton.Text = "��������";
            }
        }
        //UI�����߳�
        private void UpdateUI()
        {
            m_UIUpdateTimer.Stop();            //UI����ʱ�����ֹͣ
            double interframeDuration = m_UIUpdateTimer.Duration();//�������ʱ��

            QueryPerfCounter uiIntraFrameTimer = new QueryPerfCounter();
            uiIntraFrameTimer.Start();         //UI����ʱ����¿�ʼ

            // the frame queue is a shared resource with the FrameOfMocap delivery thread, so lock it while reading
            // note this can block the frame delivery thread.  In a production application frame queue management would be optimized.
            //֡������һ��������Դ��frameofmocap�����ߣ����԰��������Ķ�
            //ע�⣬�������ֹ֡�����̡߳�������Ӧ�ó������У����й����õ��Ż���
            //������ʹ���̵߳�ʱ��Ч����ߵķ�ʽ��Ȼ���첽���������߳�ͬʱ���У���䲻�໥�����͵ȴ���
            //������ͬ���̶߳���Ҫ����ĳ����Դ��ʱ�򣬾���Ҫͬ�������ˣ�Ҳ����˵����ͬһ����Դ���ж�д��ʱ������Ҫʹ����Դ��ͬһʱ��ֻ�ܱ�һ���̲߳�������ȷ��ÿ������������Ч��ʱ�ģ�Ҳ����֤�������ԭ���ԡ�lock��C#����õ�ͬ����ʽ
            lock (syncLock)
            {
                while (m_FrameQueue.Count > 0)   //���֡�������ݸ�������0
                {
                    m_FrameOfData = m_FrameQueue.Dequeue(); //֡���и�ֵ

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
                        UpdateDataGrid();  //��������
                        // Mocap server timestamp (in seconds)
                        //m_fLastFrameTimestamp = m_FrameOfData.fTimestamp;
                        TimestampValue.Text = m_FrameOfData.fTimestamp.ToString("F3");//֡�����е�ʱ�丳ֵ��TimestampValue
                        DroppedFrameCountLabel.Text = mDroppedFrames.ToString();  //��֡
                    }
                }
            }

            uiIntraFrameTimer.Stop();  //UI֡����ʱ��ֹͣ
            double uiIntraFrameDuration = uiIntraFrameTimer.Duration(); //����ʱ�丳ֵ
            m_UIUpdateTimer.Start();  //UI����ʱ�����

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



        //TCP/IP����  
        private void btnConnect_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(reciveAndListener);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            if (btnConnect.Text == "����")
            {
                if (txtIP.Text.Trim() == string.Empty)
                {
                    return;
                }
                if (txtPort.Text.Trim() == string.Empty)
                {
                    return;
                }
                //����̰߳󶨵ķ������в����Ļ�����ô������������ͱ�����object���ͣ����Խ�ip,�Ͷ˿ں� д��һ���ṹ����д���
                IpAndPort ipHePort = new IpAndPort();
                ipHePort.Ip = txtIP.Text;
                ipHePort.Port = txtPort.Text;
                thread.Start((object)ipHePort);
                btnConnect.Text = "�ر�";
            }
            else
            {
                thread.Abort();
                server.Stop();
                btnConnect.Text = "����";
                OutputMessage("����˹ر�");
            }
        }

        private void reciveAndListener(object ipAndPort)
        {  
            IpAndPort ipHePort = (IpAndPort)ipAndPort;
            IPAddress ip = IPAddress.Parse(ipHePort.Ip);
            server = new TcpListener(ip, int.Parse(ipHePort.Port));
            server.Start();//��������
            OutputMessage("����˿�������....");
            //  btnStart.IsEnabled = false;

            //��ȡ���ӵĿͻ��˶��� 2��
            client = server.AcceptTcpClient();
            OutputMessage("�пͻ���1�������ӣ������ѽ�����");
            client1 = server.AcceptTcpClient();
            OutputMessage("�пͻ���2�������ӣ������ѽ�����");
            //�����
            NetworkStream reciveStream = client.GetStream();
            NetworkStream reciveStream1 = client1.GetStream();

            TimeSpan ts;
            ts = System.DateTime.Now.Subtract(DateTime.Parse("1980-1-6"));
            IPDATABYTESEND.wn = 1965;
            IPDATABYTESEND.sow = 345600000+190; 
         
            #region ѭ�������ͻ��˷�������Ϣ
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
                    OutputMessage("�����쳣�����ӱ��ȹر�");
                    break;
                }
            } while (btnConnect.Text == "�ر�");

            #endregion
        }




        void Formatting_Data()  //���ݴ��
        {
            int number = 0;
               for(int i=0;i<10;i++)
               {
                   byte[] buffer = new byte[176];
                   if (IPDATABYTESEND.sow > 604800000)  //�������
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
                   CalcGlobalLocation(WX_truth_R, WY_truth_R, WZ_truth_R); //����λ�ƺ�ľ�γ��
                   IPDATABYTESEND.UAlt = GPS_H_F;
                   IPDATABYTESEND.ULat = GPS_W_F;
                   IPDATABYTESEND.ULon = GPS_J_F;
                   GPStoECEF(GPS_J_F, GPS_W_F, GPS_H_F);        //��γ��תECEF����
                   IPDATABYTESEND.ecef_pos_x = flt1 * ECEF_X + (1 - flt1) * IPDATABYTESEND.ecef_pos_x;  //X
                   IPDATABYTESEND.ecef_pos_y = flt1 * ECEF_Y + (1 - flt1) * IPDATABYTESEND.ecef_pos_y;  //Y
                   IPDATABYTESEND.ecef_pos_z = flt1 * ECEF_Z + (1 - flt1) * IPDATABYTESEND.ecef_pos_z;  //Z
                   //ECEF������ٶȼ���
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
                   //�����ݴ��뷢�ͻ�����
                   buffer = StructToBytes(IPDATABYTESEND);
                   System.Buffer.BlockCopy(buffer, 0, tmp, buffer.Length * i, buffer.Length);
                   //���ݱ���
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
 
          void Frame_head()  //���͸�GPS��֡ͷ
        {
                IPDATABYTESEND.Syn_Flag = 0xFFFF8080;
                IPDATABYTESEND.Packet_Length = 176; //�ṹ��ĳ���
                IPDATABYTESEND.Command_ID = 0x10;
                IPDATABYTESEND.Send_Node = 0x0;
                IPDATABYTESEND.Receive_Node = 0x0;
                IPDATABYTESEND.Check_Code = 0xAAAA5555;
                IPDATABYTESEND.heading = 0.000000;     //����;          
                IPDATABYTESEND.elevation = 0.000000;   //���;
                IPDATABYTESEND.bank = 0.000000;  		 //��ת��;  
                IPDATABYTESEND.ecef_vel_x = 0.0;
                IPDATABYTESEND.ecef_vel_y = 0.0;
                IPDATABYTESEND.ecef_vel_z = 0.0;
                IPDATABYTESEND.ecef_A_x = 0.000000;
                IPDATABYTESEND.ecef_A_y = 0.000000;
                IPDATABYTESEND.ecef_A_z = 0.000000;
                IPDATABYTESEND.ecef_JA_x = 0.000000;
                IPDATABYTESEND.ecef_JA_y = 0.000000;
                IPDATABYTESEND.ecef_JA_z = 0.000000;
                CalcGlobalLocation(0, 0, 0); //����λ�ƺ�ľ�γ��
                IPDATABYTESEND.UAlt = GPS_H_F;
                IPDATABYTESEND.ULat = GPS_W_F;
                IPDATABYTESEND.ULon = GPS_J_F;
                GPStoECEF(GPS_J_F, GPS_W_F, GPS_H_F);        //��γ��תECEF����
                IPDATABYTESEND.ecef_pos_x = ECEF_X;  //X
                IPDATABYTESEND.ecef_pos_y = ECEF_Y;  //Y
                IPDATABYTESEND.ecef_pos_z = ECEF_Z;  //Z
                WX_truth_Pre = IPDATABYTESEND.ecef_pos_x;
                WY_truth_Pre = IPDATABYTESEND.ecef_pos_y;
                WZ_truth_Pre = IPDATABYTESEND.ecef_pos_z;
        }

        public void theout()
        {  
            sendStream = client.GetStream();//����������ݴ������
            sendStream1 = client1.GetStream();//����������ݴ������
            byte[] buffer = tmp;//�����ݴ��������       
            sendStream.Write(buffer, 0, 1760);//����д������
            sendStream1.Write(buffer, 0, 1760);//����д������           
            Array.Clear(tmp, 0, tmp.Length);//��շ��ͻ������� 
        }

        //XY����תGPS��γ��
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
        //��γ��תECEF
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

        //����һ���� ��ѯ����    KERNEL32.DLL ��װ��������
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
            if (DATAQueue.Count < 33)  //����10��
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