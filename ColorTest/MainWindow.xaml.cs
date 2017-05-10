using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Intel.RealSense;
using Intel.RealSense.Hand;
using System.IO;

namespace ColorTest
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        SenseManager senseManager;
        /// <summary>
        /// 座標変換オブジェクト
        /// </summary>
        Projection projection;
        /// <summary>
        /// deviceのインタフェース
        /// </summary>
        Device device;
        /// <summary>
        /// 手の検出器
        /// </summary>
        HandModule handAnalyzer;
        /// <summary>
        /// 手のデータ
        /// </summary>
        HandData handData;

        const int DEPTH_WIDTH = 640;
        const int DEPTH_HEIGHT = 480;
        const int DEPTH_FPS = 30;

        const int COLOR_WIDTH = 640;
        const int COLOR_HEIGHT = 480;
        const int COLOR_FPS = 30;

        /// <summary>
        /// 日付
        /// </summary>
        static public DateTime dt = DateTime.Now;
        /// <summary>
        /// マイドキュメントのパス
        /// </summary>
        static string pathDoc = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        /// <summary>
        /// ファイル書き込み用ストリーム
        /// </summary>
        StreamWriter sw;
        /// <summary>
        /// 時間計測ストップウォッチ
        /// </summary>
        System.Diagnostics.Stopwatch StopWatch = new System.Diagnostics.Stopwatch();

        Color[] color = new Color[]{Colors.Red,
                                    Colors.OrangeRed,
                                    Colors.Orange,
                                    Colors.Yellow,
                                    Colors.YellowGreen,
                                    Colors.Green,
                                    Colors.LightBlue,
                                    Colors.Blue,
                                    Colors.Navy,
                                    Colors.Purple };

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Windowのロード時に初期化及び周期処理の登録を行う
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
            //WPFのオブジェクトがレンダリングされるタイミング(およそ1秒に50から60)に呼び出される
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        /// <summary>
        /// 機能の初期化
        /// </summary>
        private void Initialize()
        {
            try
            {
                //SenseManagerを生成
                senseManager = SenseManager.CreateInstance();

                SampleReader reader = SampleReader.Activate(senseManager);
                //カラーストリームを有効にする
                reader.EnableStream(StreamType.STREAM_TYPE_COLOR, COLOR_WIDTH, COLOR_HEIGHT, COLOR_FPS);
                //Depthストリームを有効にする
                reader.EnableStream(StreamType.STREAM_TYPE_DEPTH, DEPTH_WIDTH, DEPTH_HEIGHT, DEPTH_FPS);

                //手の検出を有効にする
                handAnalyzer = HandModule.Activate(senseManager);

                //パイプラインを初期化する
                //(インスタンスはInit()が正常終了した後作成されるので，機能に対する各種設定はInit()呼び出し後となる)
                var sts = senseManager.Init();
                if (sts < Status.STATUS_NO_ERROR) throw new Exception("パイプラインの初期化に失敗しました");

                //デバイスを取得する
                device = senseManager.CaptureManager.Device;

                //ミラー表示にする
                device.MirrorMode = MirrorMode.MIRROR_MODE_HORIZONTAL;

                //座標変換オブジェクトを作成
                projection = device.CreateProjection();

                //手の検出の初期化
                InitializeHandTracking();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Close();
            }
        }

        /// <summary>
        /// 手の検出の初期化及び設定
        /// </summary>
        private void InitializeHandTracking()
        {
            //手のデータを作成する
            handData = handAnalyzer.CreateOutput();
            if (handData == null) throw new Exception("手のデータの作成に失敗しました");

            //RealSenseカメラであれば，プロパティを設定する
            DeviceInfo dinfo;
            device.QueryDeviceInfo(out dinfo);
            if (dinfo.model == DeviceModel.DEVICE_MODEL_IVCAM)
            {
                //RealSense SDKの開発チームの感覚的に一番検出しやすい設定
                device.DepthConfidenceThreshold = 1;
                device.IVCAMFilterOption = 6;
            }
            //手の検出の設定
            //現在の設定を取得．設定値はPXCMHANDConfigurationに格納される
            HandConfiguration config = handAnalyzer.CreateActiveConfiguration();
            config.TrackingMode = TrackingModeType.TRACKING_MODE_FULL_HAND;
            Console.WriteLine(config.TrackingMode);
            config.TrackedJointsEnabled = true;
            config.EnableGesture("fist", true);
            config.ApplyChanges();
            config.Update();
        }

        /// <summary>
        /// フレームごとの更新及び個別のデータ更新処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            //try
            //{
            CanvasPoint.Children.Clear();
            //フレームを取得する
            //AcquireFrame()の引数はすべての機能の更新が終るまで待つかどうかを指定
            //ColorやDepthによって更新間隔が異なるので設定によって値を変更
            var ret = senseManager.AcquireFrame(true);
            if (ret < Status.STATUS_NO_ERROR) return;

            //フレームデータを取得する
            Sample sample = senseManager.Sample;
            if (sample != null)
            {
                //カラー画像の表示
                UpdateColorImage(sample.Color);
            }

            //手のデータを更新する
            updateHandFrame();

            //フレームを解放する
            senseManager.ReleaseFrame();
            //}
            //catch (Exception ex)
            //{
            //   MessageBox.Show(ex.Message);
            //  Close();
            //}
        }

        /// <summary>
        /// カラーイメージが更新された時の処理
        /// </summary>
        /// <param name="color"></param>
        private void UpdateColorImage(Intel.RealSense.Image colorFrame)
        {
            if (colorFrame == null) return;
            //データの取得
            ImageData data;

            //アクセス権の取得
            Status ret = colorFrame.AcquireAccess(ImageAccess.ACCESS_READ, Intel.RealSense.PixelFormat.PIXEL_FORMAT_RGB32, out data);
            if (ret < Status.STATUS_NO_ERROR) throw new Exception("カラー画像の取得に失敗");

            //ビットマップに変換する
            //画像の幅と高さ，フォーマットを取得
            var info = colorFrame.Info;

            //1ライン当たりのバイト数を取得し(pitches[0]) 高さをかける　(1pxel 3byte)
            var length = data.pitches[0] * info.height;

            //画素の色データの取得
            //ToByteArrayでは色データのバイト列を取得する．
            var buffer = data.ToByteArray(0, length);
            //バイト列をビットマップに変換
            imageColor.Source = BitmapSource.Create(info.width, info.height, 96, 96, PixelFormats.Bgr32, null, buffer, data.pitches[0]);

            //データを解放する
            colorFrame.ReleaseAccess(data);
        }

        /// <summary>
        /// Depthイメージが更新された時の処理
        /// </summary>
        /// <param name="colorFrame"></param>
        private void UpdateDepthImage(Intel.RealSense.Image depthFrame)
        {
        }

        /// <summary>
        /// 手の更新処理
        /// </summary>
        private void updateHandFrame()
        {
            handData.Update();
            //検出した手の数を取得する
            var numOfHands = handData.NumberOfHands;
            for (int i = 0; i < numOfHands; i++)
            {
                //手を取得する
                IHand hand;
                var sts = handData.QueryHandData(AccessOrderType.ACCESS_ORDER_BY_ID, i, out hand);
                if (sts < Status.STATUS_NO_ERROR) continue;

                int open = hand.Openness / 10;
                for (int j=0; j < open; j++)
                {
                    SolidColorBrush myBrush = new SolidColorBrush(color[j]);
                    myBrush.Opacity = 0.25;
                    CanvasPoint.Children.Add(CreateRectangle(imageColor.Height/open*j, imageColor.Width, imageColor.Height/open, Brushes.Black, 1.0d, myBrush));
                }

                //ジョイントデータを作成
                JointData middle = hand.TrackedJoints[JointType.JOINT_MIDDLE_TIP];
                JointData thumb = hand.TrackedJoints[JointType.JOINT_THUMB_TIP];
                JointData pinky = hand.TrackedJoints[JointType.JOINT_PINKY_TIP];

                OneJoint(middle);
                OneJoint(thumb);
                OneJoint(pinky);

                GestureData gesture;
                if (handData.IsGestureFired("fist", out gesture))
                {
                    CanvasPoint.Children.Add(CreateEllipse(
                            imageColor.Width/2,
                            imageColor.Height/2,
                            10, 10, Brushes.Black, 1.0d, Brushes.Green));
                }

                if (hand.BodySide == BodySideType.BODY_SIDE_LEFT)
                {
                    //座標を表示
                    var vec3d = Math.Sqrt(
                        Math.Pow(thumb.positionWorld.x - pinky.positionWorld.x, 2) +
                        Math.Pow(thumb.positionWorld.x - pinky.positionWorld.x, 2) +
                        Math.Pow(thumb.positionWorld.x - pinky.positionWorld.x, 2));
                    distance.Text = "thumb - pinky = " + (vec3d) * 100 + "cm";
                    depth.Text = "middle.z = " + middle.positionWorld.z * 100;
                    CanvasPoint.Children.Add(distance);
                    CanvasPoint.Children.Add(depth);

                }
            }
        }

        /// <summary>
        /// 関節を指定して表示
        /// </summary>
        /// <param name="hand"></param>
        /// <param name="jointType"></param>
        /// <returns></returns>
        void OneJoint(JointData jointData)
        {
            //Depth座標系をカラー座標系に変換する
            var depthPoint = new Point3DF32[1];
            var colorPoint = new PointF32[1];
            depthPoint[0].x = jointData.positionImage.x;
            depthPoint[0].y = jointData.positionImage.y;
            depthPoint[0].z = jointData.positionWorld.z * 1000;

            projection.MapDepthToColor(depthPoint, colorPoint);

            //円を表示
            CanvasPoint.Children.Add(CreateEllipse(
                    colorPoint[0].x,
                    colorPoint[0].y,
                    10, 10, Brushes.Black, 1.0d, Brushes.White));

            if(writeCheck.IsChecked == true)WriteFile(depthPoint, colorPoint);
        }

        /// <summary>
        /// ファイルに書き込み
        /// </summary>
        /// <param name="depth"></param>
        /// <param name="color"></param>
        void WriteFile(Point3DF32[] depth, PointF32[] color)
        {
            StopWatch.Stop();
            sw.WriteLine(
                StopWatch.Elapsed
                + "," + color[0].x
                + "," + color[0].y
                + "," + depth[0].x
                + "," + depth[0].y
                + "," + depth[0].z);
            StopWatch.Start();
        }

        /// <summary>
        /// 円生成オブジェクト
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="stroke"></param>
        /// <param name="thickness"></param>
        /// <param name="fill"></param>
        /// <returns></returns>
        private Ellipse CreateEllipse(double x, double y, double width, double height, Brush stroke, double thickness, Brush fill)
        {
            Ellipse ellipse = new Ellipse();
            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            ellipse.Width = width;
            ellipse.Height = height;
            ellipse.Stroke = stroke;
            ellipse.StrokeThickness = thickness;
            ellipse.Fill = fill;
            return ellipse;
        }
        
        /// <summary>
        /// 四角生成オブジェクト
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="stroke"></param>
        /// <param name="thickness"></param>
        /// <param name="fill"></param>
        /// <returns></returns>
        private Rectangle CreateRectangle(double y ,double width, double height, Brush stroke, double thickness, Brush fill)
        {
            Rectangle rect = new Rectangle();
            Canvas.SetTop(rect, y);
            rect.Width = width;
            rect.Height = height;
            rect.Stroke = stroke;
            rect.StrokeThickness = thickness;
            rect.Fill = fill;
            return rect;
        }

        /// <summary>
        /// 桁の補正
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public String digits(int date)
        {
            if (date / 10 == 0) return "0" + date;
            else return date.ToString();
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        private void Uninitialize()
        {
            if (sw != null){
                sw.Close();
                sw.Dispose();
            }

            StopWatch.Stop();

            senseManager.Dispose();
            senseManager = null;

            projection.Dispose();
            projection = null;

            handData.Dispose();
            handData = null;

            handAnalyzer.Dispose();
            handAnalyzer = null;
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            Uninitialize();
        }

        private void writeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (sw == null)
            {
                string date = dt.Year + digits(dt.Month) + digits(dt.Day) + digits(dt.Hour) + digits(dt.Minute);
                string path = pathDoc + "/RS";
                //Directory.CreateDirectory(path);
                //ファイルを用意
                sw = new StreamWriter(path + "/" + date + ".csv", false);
                StopWatch.Start();
            }
        }
    }
}