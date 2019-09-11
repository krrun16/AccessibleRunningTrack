﻿using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Util;
using System.IO;
using Android;
using Android.Hardware.Camera2;
using Android.Graphics;
using System.Collections.Generic;
using Android.Media;
using System.Threading;
using System.Collections.Concurrent;
using Android.Renderscripts;
using Type = Android.Renderscripts.Type;
using Android.Content;
using Android.Speech.Tts;
using Android.Runtime;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using Size = Org.Opencv.Core.Size;
using Android.Hardware;
using Xamarin.Essentials;

namespace CustomVision //name of our app
{
    /* We never call this code explictly
     * triggered based on the state of the camera
     * This code waits for something to happen to the camera
     */
    internal class StateCallback : CameraDevice.StateCallback
    {
        public StateCallback()
        {
        }

        // When the camera opens, set a global variable "cameraDevice"
        // For example, I set that it is the "back camera"
        // Then, it creates a "preview session," which means that the camera streams to the phone screen
        public override void OnOpened(CameraDevice camera)
        {
            MainActivity.cameraDevice = camera;
            if(MainActivity.cameraDevice != null)
            {
                MainActivity.CreatePreviewSession();
            }
        }

        // Without bugs, this should not get called
        // When the camera disconnects from the app
        // We close the camera, and we unset that variable
        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            MainActivity.cameraDevice = null;
        }

        // Without bugs, this should not get called
        // If the camera experiences an error
        // We close the camera, and we unset that variable
        public override void OnError(CameraDevice camera, Android.Hardware.Camera2.CameraError error)
        {
            camera.Close();
            MainActivity.cameraDevice = null;
        }
    }

    /* We do not explictly call this code
     * This is called when something happens to the camera in preparation to "capture"
     */
    internal class CameraCaptureStateCallback : CameraCaptureSession.StateCallback
    {
        private CaptureRequest captureRequest;

        // We do not call this method explictly
        // When the camera has been configured (from createpreview method)
        // It will create a "capture request" to capture anything the camera sees
        public override void OnConfigured(CameraCaptureSession session)
        {
            // if the camera is null (note this shouldn't happen)
            if (MainActivity.cameraDevice == null)
            {
                return;
            }

            try
            {
                captureRequest = MainActivity.captureRequestBuilder.Build(); // you have to create a capture request in order to get photos
                MainActivity.cameraCaptureSession = session; // variable with the current camera capture "session"
                MainActivity.cameraCaptureSession.SetRepeatingRequest(captureRequest,
                        null, MainActivity.backgroundHandler); // run the session infinitely long
                Interlocked.Exchange(ref MainActivity.canProcessImage, 1); // go ahead and classify images, equivalent canProcessImage = 1;
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }

        // If configuration fails, close the session
        // If there is no bugs, this shouldn't happen
        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            session.Close();
        }
    }

    [Activity(Label = "@string/app_name", MainLauncher = false, Icon = "@mipmap/icon", Theme = "@style/MyTheme", ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : AppCompatActivity, TextureView.ISurfaceTextureListener, 
        Android.Speech.Tts.TextToSpeech.IOnInitListener, ILoaderCallbackInterface, ISensorEventListener
    {
        private static Context context;
        public static int cameraFacing;
        public static TextureView textureView;
        public static Android.Util.Size previewSize;
        public static ImageReader imageReader;
        internal static CameraDevice cameraDevice;
        private HandlerThread backgroundThread;
        public static Handler backgroundHandler;
        public static CaptureRequest.Builder captureRequestBuilder;
        internal static CameraCaptureSession cameraCaptureSession;
        public string CameraId { get; private set; }
        private int DSI_width;
        public static int canProcessImage = 0;
        public static BlockingCollection<BitmapPrefix> bc = new BlockingCollection<BitmapPrefix>();
        private static Task task;
        private static Task task2;
        private static readonly object locker = new object();
        public static string PreviousText = "noLabel";
        private static string previousOutput = null;
        private static string previousLabel = null;
        private static string priorToParallel = null;
        private static readonly List<string> storeWindow = new List<string>();
        private static Android.Speech.Tts.TextToSpeech tts;
        private static readonly int WINDOW_SIZE = 20;
        private static readonly int IN_LANE_WINDOW_SIZE = 5;
        private static MediaPlayer inLane;
        private static MediaPlayer left;
        private static MediaPlayer right;
        private static MediaPlayer go;
        private static MediaPlayer slightRight;
        private static MediaPlayer slightLeft;
        private static MediaPlayer littleRight;
        private static MediaPlayer littleLeft;

        private static System.Timers.Timer timer;
        public static bool isReady = false;
        public static bool wait = false;
        public static bool backCamera = false;
        public static string sdCardPath = "";

        static readonly object _syncLock = new object();
        private SensorManager sensorManager;
        private Sensor gsensor;
        private float[] mGravity = new float[3];
        public static double rotatedAngle;

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState); // add this line to your code, it may also be called: bundle
            context = ApplicationContext;
            wait = Intent.GetBooleanExtra("wait", false);
            backCamera = Intent.GetBooleanExtra("backCamera", false);
            sdCardPath = Intent.GetStringExtra("sdCardPath");

            // set parameters to 25%, no tilt, front camera no matter what
            backCamera = false;

            if (backCamera)
            {
                cameraFacing = (int)LensFacing.Back;
            } else
            {
                cameraFacing = (int)LensFacing.Front;
            }
            
            sensorManager = (SensorManager)GetSystemService(SensorService);
            gsensor = sensorManager.GetDefaultSensor(SensorType.Accelerometer);

            textureView = new TextureView(this);
            SetContentView(textureView);
            Window.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
            tts = new Android.Speech.Tts.TextToSpeech(this, this);

            inLane = MediaPlayer.Create(this, Resource.Raw.sound);
            left = MediaPlayer.Create(this, Resource.Raw.left90);
            right = MediaPlayer.Create(this, Resource.Raw.right90);
            go = MediaPlayer.Create(this, Resource.Raw.go);
            slightRight = MediaPlayer.Create(this, Resource.Raw.right45);
            slightLeft = MediaPlayer.Create(this, Resource.Raw.left45);
            littleRight = MediaPlayer.Create(this, Resource.Raw.righttiny);
            littleLeft = MediaPlayer.Create(this, Resource.Raw.lefttiny);

            if (wait)
            {
                SaveLog("trial selected", DateTime.Now, 0);
                var level = Battery.ChargeLevel;
                SaveLog("battery level = " + level, DateTime.Now, 0);
                timer = new System.Timers.Timer
                {
                    Interval = 30000,
                    Enabled = true
                };
                timer.Elapsed += (object sender, System.Timers.ElapsedEventArgs e) =>
                {
                    timer.Stop();
                    Log.Debug("Uiowa","Timer finished!");
                    //isReady = true; //after 30 secs, ready to process the image
                    //Speak("GO", 0); 
                    SaveLog("speak " + "GO", DateTime.Now, 0);
                    go.Start(); //Say "Go" before start processing
                    go.Completion += delegate
                    {
                        isReady = true; //after 30 secs, ready to process the image
                    };
                    timer.Dispose();
                };
                timer.Start();
            } else
            {
                SaveLog("tutorial selected", DateTime.Now, 0);
                var level = Battery.ChargeLevel;
                SaveLog("battery level = " + level, DateTime.Now, 0);
                isReady = true; // for tutorial button wait = false, directly start processing
            }
            
        }

        /*public static void Speak(string CurrentText, int prefix)
        {
            if (!tts.IsSpeaking) 
            {
                SaveLog("speak " + CurrentText, DateTime.Now, prefix);
                tts.Speak(CurrentText, QueueMode.Flush, null, null);
            }
        }*/

        protected override void OnResume()
        {
            base.OnResume();
            if (!OpenCVLoader.InitDebug())
            {
                Log.Debug("Iowa", "Internal OpenCV library not found. Using OpenCV Manager for initialization");
                OpenCVLoader.InitAsync(OpenCVLoader.OpencvVersion300, this, this);
            }
            else
            {
                Log.Debug("Iowa", "OpenCV library found inside package. Using it!");
                OnManagerConnected(LoaderCallbackInterface.Success);
            }
            OpenBackgroundThread();

            sensorManager.RegisterListener(this, gsensor, SensorDelay.Fastest);

            if (textureView.IsAvailable)
            {
                if (cameraDevice == null)
                {
                    CameraManager cameraManager = (CameraManager)GetSystemService(CameraService);
                    SetUpCamera(cameraManager);
                    OpenCamera(cameraManager);
                }
            } else
            {
                textureView.SurfaceTextureListener = this;
            }
        }

        public static void BC_SaveImages()
        {
            void action()
            {
                try
                {
                    while (true)
                    {
                        BitmapPrefix bitmapPrefix = bc.Take();
                        MemoryStream byteArrayOutputStream = new MemoryStream();
                        bitmapPrefix.Bitmap.Compress(Bitmap.CompressFormat.Png, 100,
                            byteArrayOutputStream);
                        SaveLog("created png", DateTime.Now, bitmapPrefix.Prefix);
                        byte[] png = byteArrayOutputStream.ToArray();
                        SaveBitmap(png, bitmapPrefix.Prefix);
                        bitmapPrefix.Bitmap.Dispose(); //release the memory to handle OutOfMemory error
                    }
                }
                catch (InvalidOperationException)
                {

                }
            }

            task = Task.Factory.StartNew(action);
            task2 = Task.Factory.StartNew(action);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            tts.Stop();
            tts.Shutdown();
            CloseCamera();
            CloseBackgroundThread();
            FinishAffinity();
            go.Stop();
            go.Release();
            inLane.Stop();
            inLane.Release();
            left.Stop();
            left.Release();
            right.Stop();
            right.Release();
            slightRight.Stop();
            slightRight.Release();
            slightLeft.Stop();
            slightLeft.Release();
            littleRight.Stop();
            littleRight.Release();
            littleLeft.Stop();
            littleLeft.Release();

            System.Environment.Exit(0);
        }

        protected override void OnStop()
        {
            base.OnStop();
            tts.Stop();
            tts.Shutdown();
            CloseCamera();
            CloseBackgroundThread();
            FinishAffinity();
            System.Environment.Exit(0);
        }

        protected override void OnPause()
        {
            CloseCamera();
            if (tts != null)
            {
                tts.Stop();
                tts.Shutdown();
            }
            CloseBackgroundThread();
            base.OnPause();
            FinishAffinity();
            System.Environment.Exit(0);
        }

        private void CloseCamera()
        {
            var level = Battery.ChargeLevel;
            SaveLog("battery level = " + level, DateTime.Now, 0);
            bc.CompleteAdding();
            task.Wait();
            task2.Wait();
            if (cameraCaptureSession != null)
            {
                cameraCaptureSession.Close();
                cameraCaptureSession = null;
            }

            if (cameraDevice != null)
            {
                cameraDevice.Close();
                cameraDevice = null;
            }
            if (imageReader != null)
            {
                imageReader.Close();
                imageReader = null;
            }
        }

        public static void CreatePreviewSession()
        {
            try
            {
                BC_SaveImages();
                
                SurfaceTexture surfaceTexture = textureView.SurfaceTexture;
                surfaceTexture.SetDefaultBufferSize(previewSize.Width, previewSize.Height);
                Surface previewSurface = new Surface(surfaceTexture);
                captureRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                captureRequestBuilder.AddTarget(previewSurface);
                captureRequestBuilder.AddTarget(imageReader.Surface);
                cameraDevice.CreateCaptureSession(new List<Surface>() { previewSurface,
                    imageReader.Surface }, new CameraCaptureStateCallback(), backgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }

        private void OpenBackgroundThread()
        {
            backgroundThread = new HandlerThread("camera_background_thread");
            backgroundThread.Start();
            backgroundHandler = new Handler(backgroundThread.Looper);
        }

        private void CloseBackgroundThread()
        {
            if (backgroundHandler != null)
            {
                backgroundThread.QuitSafely();
                backgroundThread = null;
                backgroundHandler = null;
            }
        }

        private void SetAspectRatioTextureView(int ResolutionWidth, int ResolutionHeight)
        {
            if (ResolutionWidth > ResolutionHeight)
            {
                int newWidth = DSI_width;
                int newHeight = ((DSI_width * ResolutionWidth) / ResolutionHeight);
                UpdateTextureViewSize(newWidth, newHeight);
            }
            else
            {
                int newWidth = DSI_width;
                int newHeight = DSI_width * ResolutionHeight / ResolutionWidth;
                UpdateTextureViewSize(newWidth, newHeight);
            }
        }

        public static void UpdateTextureViewSize(int viewWidth, int viewHeight)
        {
            textureView.LayoutParameters.Width = viewWidth;
            textureView.LayoutParameters.Height= viewHeight;
            textureView.RequestLayout();
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int w, int h)
        {
            if (cameraDevice == null)
            {
                SetUpCamera((CameraManager)GetSystemService(CameraService));
                OpenCamera((CameraManager)GetSystemService(CameraService));
            }
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
            return false;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
        {
            // camera takes care of this
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
        {
        }

        //Update the result in the list
        public static void StoreResult(string res)
        {
            if (storeWindow.Count > WINDOW_SIZE)
            {
                storeWindow.RemoveAt(0); //remove the first result
            }
            storeWindow.Add(res); //add the latest result
        }

        //find the most occured labels
        public static string GetTopResult(List<string> labels)
        {
            for(int i = 0; i < labels.Count; ++i)
            {
                int count = 0;
                for(int j = 0; j < storeWindow.Count; ++j)
                {
                    if (storeWindow[j] == labels[i])
                    {
                        count++;
                    }
                }
                if (labels[i] == "inlane" && count > IN_LANE_WINDOW_SIZE/2)
                {
                    return labels[i];
                }

                if (count > WINDOW_SIZE/2)
                {
                    return labels[i];
                }
            }
            return null;
        }

        public static void SaveBitmap(byte[] data, int prefix) {
            lock (locker)
            {
                DateTime currentDate = DateTime.Now;
                long ts = currentDate.Ticks;
                string fileName = prefix + ".  " + currentDate.TimeOfDay + ".png";
                string FilePath = System.IO.Path.Combine(sdCardPath, fileName);

                if (!File.Exists(FilePath))
                {
                    File.WriteAllBytes(FilePath, data);
                    SaveLog("saved image", DateTime.Now, prefix);
                }
            }
        }

        // credit https://stackoverflow.com/questions/44652828/camera2-api-imageformat-yuv-420-888-results-on-rotated-image
        public static Bitmap YUV_420_888_toRGBIntrinsics(Image image)
        {
            if (image == null) return null;

            int W = image.Width;
            int H = image.Height;

            Image.Plane Y = image.GetPlanes()[0];
            Image.Plane U = image.GetPlanes()[1];
            Image.Plane V = image.GetPlanes()[2];

            int Yb = Y.Buffer.Remaining();
            int Ub = U.Buffer.Remaining();
            int Vb = V.Buffer.Remaining();

            byte[] data = new byte[Yb + Ub + Vb];

            Y.Buffer.Get(data, 0, Yb);
            V.Buffer.Get(data, Yb, Vb);
            U.Buffer.Get(data, Yb + Vb, Ub);
            RenderScript rs = RenderScript.Create(context);
            ScriptIntrinsicYuvToRGB yuvToRgbIntrinsic = ScriptIntrinsicYuvToRGB.Create(
                rs, Element.U8_4(rs));

            using (Type.Builder builder = new Type.Builder(rs, Element.U8(rs)))
            {
                using (Type.Builder yuvType = builder.SetX(data.Length))
                {
                    Allocation inAll = Allocation.CreateTyped(rs, yuvType.Create());
                    inAll.CopyFromUnchecked(data);
                    yuvToRgbIntrinsic.SetInput(inAll);
                }
            }

            Bitmap bmpout = Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888);
            using (Type.Builder builder1 = new Type.Builder(rs, Element.RGBA_8888(rs)))
            {
                using (Type.Builder rgbaType = builder1.SetX(W).SetY(H))
                {
                    Allocation outAll = Allocation.CreateTyped(rs, rgbaType.Create());
                    yuvToRgbIntrinsic.ForEach(outAll);

                    outAll.CopyTo(bmpout);
                }
            }
            image.Close();
            yuvToRgbIntrinsic.Dispose();
            return bmpout;
        }

        public static void SaveLog(string label, DateTime currentTime, int prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + ": " + label;
            string filePath = System.IO.Path.Combine(sdCardPath,"0_log.txt");
            lock (locker)
            {
                using (StreamWriter write = new StreamWriter(filePath, true))
                {
                    write.Write(msg + "\n");
                }
            }
        }


        public static void SaveLog_thres(double label, DateTime currentTime, int prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + "_" + label;
            string filePath = System.IO.Path.Combine(sdCardPath,"0_threslog.txt");
            lock (locker)
            {
                using (StreamWriter write = new StreamWriter(filePath, true))
                {
                    write.Write(msg + "\n");
                }
            }
        }

        private void SetUpCamera(CameraManager cameraManager)
        {
            try {
                foreach (string cameraId in cameraManager.GetCameraIdList()) {
                    CameraCharacteristics cameraCharacteristics =
                            cameraManager.GetCameraCharacteristics(cameraId);
                    if ((int)cameraCharacteristics.Get(CameraCharacteristics.LensFacing) ==
                            cameraFacing)
                        {

                        CameraId = cameraId;
                        Android.Hardware.Camera2.Params.StreamConfigurationMap 
                            streamConfigurationMap = 
                            (Android.Hardware.Camera2.Params.StreamConfigurationMap) 
                            cameraCharacteristics.Get(
                                CameraCharacteristics.ScalerStreamConfigurationMap);
                        DisplayMetrics displayMetrics = Resources.DisplayMetrics;
                        DSI_width = displayMetrics.WidthPixels;
                        previewSize = streamConfigurationMap.GetOutputSizes(
                            (int)ImageFormatType.Yuv420888)[0];

                        SetAspectRatioTextureView(previewSize.Width, previewSize.Height);

                        imageReader = ImageReader.NewInstance(previewSize.Width, previewSize.Height, 
                            ImageFormatType.Yuv420888, 1);
                        imageReader.SetOnImageAvailableListener(new ImageAvailableListener(),
                            backgroundHandler);
                    }
                } 
            } catch (CameraAccessException e) {
                e.PrintStackTrace();
            }
        }

        private void OpenCamera(CameraManager cameraManager)
        {
            try
            {
                if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera)
                        == Permission.Granted)
                {
                    cameraManager.OpenCamera(CameraId, new StateCallback(), backgroundHandler);
                }
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }

        public void OnInit([GeneratedEnum] OperationResult status)
        {
            if (!status.Equals(OperationResult.Success))
                Log.Error("Uiowa", "Text to speech not initialized!");
        }

        public void OnManagerConnected(int p0)
        {
            switch (p0)
            {
                case LoaderCallbackInterface.Success:
                    Log.Debug("Iowa", "OpenCV loaded successfully");
                    
                    break;
                default:
                    break;
            }
        }

        public void OnPackageInstall(int p0, IInstallCallbackInterface p1)
        {
            
        }

        public static Lines DeriveLineInfo(double x1, double x2, double y1, double y2)
        {
            Lines line = new Lines();
            double m = (y2 - y1) / (x2 - x1);
            line.b = y1 - (m * x1);
            line.m = -1 * m;
            line.y = 1;
            return line;
        }

        public static Mat Solve2D(Lines[] lines)
        {
            Mat a = new Mat(lines.Length, 2, CvType.Cv32f);
            Mat b = new Mat(lines.Length, 1, CvType.Cv32f);

            for (int i = 0; i < lines.Length; i++)
            {
                a.Put(i, 0, lines[i].m);
                a.Put(i, 1, lines[i].y);
                b.Put(i, 0, lines[i].b);
            }

            Mat dst = new Mat();

            Core.Solve(a, b, dst, Core.DecompSvd);
            return dst;
        }

        public static string CheckPriorDirection()
        {
            int rightCount = 0;
            int leftCount = 0;
            for (int j = storeWindow.Count - 1; j >= 0; j--)
            {
                if (storeWindow[j] == "little right" || storeWindow[j] == "slight right")
                {
                    rightCount++;
                }
                if (storeWindow[j] == "little left" || storeWindow[j] == "slight left")
                {
                    leftCount++;
                }
                if (storeWindow[j] == "inlane")
                {
                    if (rightCount > leftCount)
                    {
                        return "right";
                    } else
                    {
                        return "left";
                    }
                }
            }
            if (rightCount > leftCount)
            {
                return "right";
            }
            else
            {
                return "left";
            }
        }

        public static void ImplementImageProcessing(Bitmap resizedBitmap,int prefix, bool speak)
        {
            // setting up image labels
            List<string> labels = new List<string>();
            string[] input = { "inlane", "left", "right", "slight right", "slight left", "little right",
                "little left", "parallel" };
            labels.AddRange(input);
            string currentLabel = "";

            // preparing image
            Mat imgMat = new Mat();
            Utils.BitmapToMat(resizedBitmap, imgMat);
            imgMat = DetectColor(imgMat);

            // blur image
            Mat blurMat = new Mat();
            Size ksize = new Size(9, 9);
            Imgproc.GaussianBlur(imgMat, blurMat, ksize, 0);

            // Create a gray image and take the average of the image
            Mat grayImg = new Mat();
            Imgproc.CvtColor(imgMat, grayImg, Imgproc.ColorRgb2gray);
            Scalar mean_scalar = Core.Mean(grayImg);

            // Detect edges
            Mat cannyMat = new Mat();
            // taken from https://stackoverflow.com/questions/24862374/canny-edge-detector-threshold-values-gives-different-result
            Imgproc.Canny(blurMat, cannyMat, mean_scalar.Val[0] * 0.66, mean_scalar.Val[0] * 1.33);

            //Hough Line detection
            Mat lines = new Mat();
            int threshold = 50;
            int minLineSize = 50;
            int lineGap = 8;
            Imgproc.HoughLinesP(cannyMat, lines, 1, Math.PI / 180, threshold, minLineSize, lineGap);

            if (lines.Rows() > 0) // lines exist in the image
            {
                Lines[] lineInfos = new Lines[lines.Rows()];
                int idx = 0;

                double minSlope = 99999;
                double maxSlope = -99999;

                for (int x = 0; x < lines.Rows(); x++)
                {
                    double[] vec = lines.Get(x, 0);
                    double x1 = vec[0],
                           y1 = vec[1],
                           x2 = vec[2],
                           y2 = vec[3];
                    using (Org.Opencv.Core.Point start = new Org.Opencv.Core.Point(x1, y1))
                    {
                        using (Org.Opencv.Core.Point end = new Org.Opencv.Core.Point(x2, y2))
                        {
                            double coefficient = .75; // we want lines that have at least 1 point in the top 3/4 of image 

                            if (x1 != x2 && Math.Min(y1, y2) < coefficient * resizedBitmap.Height)
                            // we choose to reject perfectly vertical lines because the slope and
                            // y-intercepts are both infinity. This throws off the linear solver and returns 0,0
                            // slope is rise over run, so it would be rise/0 = infinity
                            {
                                Imgproc.Line(imgMat, start, end, new Scalar(0, 255, 0, 255), 1);
                                lineInfos[idx] = DeriveLineInfo(x1, x2, y1, y2);
                                if (lineInfos[idx].m < minSlope)
                                {
                                    minSlope = lineInfos[idx].m;
                                }
                                if (lineInfos[idx].m > maxSlope)
                                {
                                    maxSlope = lineInfos[idx].m;
                                }
                                idx++;
                            }
                        }
                    }
                }

                if (lineInfos.Length >= 2) // since we have two unknowns, x and y, we need at least 2 lines
                {
                    bool isParallel = false;
                    if (maxSlope - minSlope < .1)
                    {
                        // lines are parallel
                        SaveLog("lines are parallel", DateTime.Now, prefix);
                        if (previousLabel != "parallel")
                        {
                            priorToParallel = CheckPriorDirection();
                        }
                        isParallel = true;
                    }

                    Mat answer = Solve2D(lineInfos);
                    Org.Opencv.Core.Point point = new Org.Opencv.Core.Point
                    {
                        X = answer.Get(0, 0)[0],
                        Y = answer.Get(1, 0)[0]
                    };
                    Imgproc.Circle(imgMat, point, 1, new Scalar(0, 255, 0, 255), 5);
                    double intersect_dist = point.X;
                    SaveLog_thres(intersect_dist, DateTime.Now, prefix);

                    double inlane_min = 84;
                    double inlane_max = 140;
                    double little_right_min = 0;
                    double slight_right_min = -576;
                    double little_left_max = 224;
                    double slight_left_max = 800;

                    if (!isParallel)
                    {
                        if (intersect_dist < inlane_min) // veering right
                        {
                            if (intersect_dist > little_right_min)
                            {
                                currentLabel = labels[5]; // little right
                            }
                            else if (intersect_dist > slight_right_min)
                            {
                                currentLabel = labels[3]; // slight right
                            }
                            else
                            {
                                currentLabel = labels[2];
                            }

                        }
                        else if (intersect_dist > inlane_max) // veering left
                        {
                            if (intersect_dist < little_left_max)
                            {
                                currentLabel = labels[6]; // little left
                            }
                            else if (intersect_dist < slight_left_max)
                            {
                                currentLabel = labels[4]; // slight left
                            }
                            else
                            {
                                currentLabel = labels[1];
                            }
                        }
                        else if (intersect_dist >= inlane_min && intersect_dist <= inlane_max) // in lane
                        {
                            currentLabel = labels[0];
                        }
                    } else
                    {
                        currentLabel = labels[7];
                    }
                }
                else
                {
                    currentLabel = "not_enough_lines";
                }

                previousLabel = currentLabel;

                //convert Mat to Bitmap again
                Utils.MatToBitmap(imgMat, resizedBitmap);

                //Release all Mats
                imgMat.Release();
                blurMat.Release();
                grayImg.Release();
                cannyMat.Release();
                lines.Release();

                StoreResult(currentLabel);
                SaveLog("current result: " + currentLabel, DateTime.Now, prefix);
                string bestResultSoFar = GetTopResult(labels);
                string curOutput;
                if (bestResultSoFar == null)
                {
                    curOutput = currentLabel;
                }
                else
                {
                    SaveLog("best result found from image processing: " + bestResultSoFar, DateTime.Now, prefix);
                    curOutput = bestResultSoFar;
                }

                if (speak)
                {
                    if (curOutput == labels[2]) //going right
                    {
                        //speaking left
                        InterruptVoice(left);
                        SaveLog("speak " + "left", DateTime.Now, prefix);
                        left.Start();
                    }
                    else if (curOutput == labels[1]) //going left
                    {
                        //speaking right
                        InterruptVoice(right);
                        SaveLog("speak " + "right", DateTime.Now, prefix);
                        right.Start();
                    }

                    else if (curOutput == labels[4]) //going slight left
                    {
                        //speaking slight right
                        InterruptVoice(slightRight);
                        SaveLog("speak " + "slight right", DateTime.Now, prefix);
                        slightRight.Start();
                    }
                    else if (curOutput == labels[3]) //going slight right
                    {
                        //speaking slight left
                        InterruptVoice(slightLeft);
                        SaveLog("speak " + "slight left", DateTime.Now, prefix);
                        slightLeft.Start();
                    }
                    else if (curOutput == labels[6]) //going little left
                    {
                        //speaking little right
                        InterruptVoice(littleRight);
                        SaveLog("speak " + "little right", DateTime.Now, prefix);
                        littleRight.Start();
                    }
                    else if (curOutput == labels[5]) //going little right
                    {
                        //speaking little left
                        InterruptVoice(littleLeft);
                        SaveLog("speak " + "little left", DateTime.Now, prefix);
                        littleLeft.Start();
                    }

                    else if (curOutput == labels[7]) // parallel - gone too far
                    {
                        if (priorToParallel == "right") // gone too far right
                        {
                            // speaking left
                            InterruptVoice(left);
                            SaveLog("speak left", DateTime.Now, prefix);
                            left.Start();
                        }
                        else if (priorToParallel == "left") // gone too far left
                        {
                            // speaking right
                            InterruptVoice(right);
                            SaveLog("speak right", DateTime.Now, prefix);
                            right.Start();
                        }
                    }

                    else if (curOutput == labels[0])// going inlane
                    {
                        if (previousOutput != curOutput && previousOutput != null) //checking if previous label = left or right
                        {
                            // play ding
                            SaveLog("in lane ding play", DateTime.Now, prefix);
                            InterruptVoice(inLane);
                            inLane.Start();
                        }
                    }
                    else
                    {
                        SaveLog("no conclusion made", DateTime.Now, prefix);
                    }

                    previousOutput = curOutput; // store previous output
                }

            } else
            {
                SaveLog("no lines detected", DateTime.Now, prefix);
            }
        }

        private static void InterruptVoice(MediaPlayer player)
        {
            if (player != left && left.IsPlaying)
            {
                left.Stop();
                left.Prepare();
            }
            else if (player != right && right.IsPlaying)
            {
                right.Stop();
                right.Prepare();
            }
            else if (player != slightRight && slightRight.IsPlaying)
            {
                slightRight.Stop();
                slightRight.Prepare();
            }
            else if (player != slightLeft && slightLeft.IsPlaying)
            {
                slightLeft.Stop();
                slightLeft.Prepare();
            }
            else if (player != littleRight && littleRight.IsPlaying)
            {
                littleRight.Stop();
                littleRight.Prepare();
            }
            else if (player != littleLeft && littleLeft.IsPlaying)
            {
                littleLeft.Stop();
                littleLeft.Prepare();
            }
        }

        private static Mat DetectColor(Mat img)
        {
            Mat mask1 = new Mat();
            Mat mask2 = new Mat();
            Mat hsvImg = new Mat();
            Imgproc.CvtColor(img, hsvImg, Imgproc.ColorRgb2hsv, 0);
            Core.InRange(hsvImg, new Scalar(0, 50, 20), new Scalar(10, 255, 255), mask1);
            Core.InRange(hsvImg, new Scalar(160, 50, 20), new Scalar(180, 255, 255), mask2);
            Mat output = new Mat();
            Core.Bitwise_or(mask1, mask2, mask1);
            Core.Bitwise_and(img, img, output, mask1);
            mask1.Release();
            mask2.Release();
            hsvImg.Release();
            return output;
        }

        void ISensorEventListener.OnAccuracyChanged(Sensor sensor, SensorStatus accuracy)
        {
            
        }

        void ISensorEventListener.OnSensorChanged(SensorEvent e)
        {
            lock (_syncLock)
            {
                if (e.Sensor.Type == Android.Hardware.SensorType.Accelerometer)
                {
                    List<float> tmp = new List<float>(e.Values);
                    mGravity = tmp.ToArray();

                    // FINALLY found a useful solution; thank goodness for this particular stack overflow
                    // https://stackoverflow.com/questions/11175599/how-to-measure-the-tilt-of-the-phone-in-xy-plane-using-accelerometer-in-android/15149421#15149421
                    float norm_of_gravity = (float)Math.Sqrt(Math.Pow(mGravity[0], 2) + Math.Pow(mGravity[1], 2) + 
                        Math.Pow(mGravity[2], 2));
                    //normalizing gravity to ensure no NaN answers for atan
                    mGravity[0] = mGravity[0] / norm_of_gravity;
                    mGravity[1] = mGravity[1] / norm_of_gravity;
                    mGravity[2] = mGravity[2] / norm_of_gravity;

                    rotatedAngle = 180 * (Math.Atan2(mGravity[0], mGravity[1]) / Math.PI);
                    Log.Debug("IOWA", "rotatedAngle: " + rotatedAngle);
                }
            }
        }
    }

    public struct Lines
    {
        public double m, b, y;

        public Lines(double slope, double intercept, double yc)
        {
            m = slope;
            b = intercept;
            y = yc;
        }
    }

    public struct Intersect
    {
        public double x, y;

        public Intersect(double xPos, double yPos)
        {
            x = xPos;
            y = yPos;
        }
    }

    public class BitmapPrefix
    {
        public Bitmap Bitmap { get; set; }
        public int Prefix { get; set; }

        public BitmapPrefix() { }

        public BitmapPrefix(Bitmap bitmap, int prefix)
        {
            Bitmap = bitmap;
            Prefix = prefix;
        }
    }

    /* We don't explicitly call this code
     * Any time there is an image available on the screen, we could classify the image
     */
    internal class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private int PREFIX = 0;
        // We don't explicitly call this code
        // If there is a picture available...
        public void OnImageAvailable(ImageReader reader)
        {
            if (1 == Interlocked.CompareExchange(ref MainActivity.canProcessImage, 0, 1)) // if canProcessImage = 1 -> set canProcessImage = 0 immediately and return 1
                // if canProcessImage = 0, so it isn't equal to the third parameter, the method returns 0.
            {
                if (!MainActivity.bc.IsAddingCompleted) // **TODO
                {
                    int prefix = Interlocked.Increment(ref PREFIX); // thread safe way to create the number that is attached to the photo and the log file
                    // equivalent to prefix = prefix + 1;
                    MainActivity.SaveLog("can process image", DateTime.Now, prefix); // write when the photo can be processed to the log
                    Image image = reader.AcquireNextImage(); // get the next picture from my screen
                    // Process the image
                    if (image == null) // this shouldn't happen, but if it does returns to avoid further issues
                    {
                        return;
                    }

                    Bitmap bitmap = null;
                    if(MainActivity.isReady)
                    {
                        // textureview is the region of the screen that contains the camera, 
                        // so get the picture with the same dimensions as the screen
                        bitmap = MainActivity.textureView.GetBitmap(MainActivity.textureView.Width, 
                            MainActivity.textureView.Height);
                        if (MainActivity.cameraFacing == (int)LensFacing.Front)
                        {
                            Log.Debug("iowa", "this is the front camera.");
                            int cx = bitmap.Width / 2;
                            int cy = bitmap.Height / 2;
                            Matrix matrix = new Matrix();
                            matrix.PostScale(-1, 1, cx, cy);
                            bitmap = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                        }
                    }

                    if (bitmap != null)
                    {
                        int inputsize = 224;
                        //resize the bitmap
                        using (Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, inputsize, inputsize, false))
                        {
                            Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false);
                            Bitmap preOpenCvBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false);
                            int w = resizedBitmap.Width;
                            int h = resizedBitmap.Height;
                            BitmapPrefix bitmapPrefix = new BitmapPrefix(resizedBitmap, prefix);

                            // adding the track photo after making it smaller and square, so that we have it for further analysis
                            if (!MainActivity.bc.IsAddingCompleted)
                            {
                                BitmapPrefix preOpenCVBitmapPrefix = new BitmapPrefix(preOpenCvBitmap, prefix);
                                MainActivity.bc.Add(preOpenCVBitmapPrefix);
                            }

                            MainActivity.ImplementImageProcessing(resizedBitmap, prefix, true);
                            MainActivity.SaveLog("created bitmap", DateTime.Now, prefix); // write when the bitmap is created to the log

                            if (!MainActivity.bc.IsAddingCompleted)
                            {
                                MainActivity.bc.Add(bitmapPrefix);
                            }
                        }
                    }
                    image.Close(); // This closes the image so the phone no longer has to hold onto 
                    // it, otherwise it will slow the system and stop collecting pictures
                }   
                Interlocked.Exchange(ref MainActivity.canProcessImage, 1); // equivalent to canProcessImage = 1; meaning anyone else can come with their image
            }
        }
    }
}