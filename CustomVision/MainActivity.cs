using System;
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
using Java.Nio;
using Android.Renderscripts;
using Type = Android.Renderscripts.Type;
using Android.Content;
using Android.Speech.Tts;
using Android.Runtime;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using Size = Org.Opencv.Core.Size;

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
        public override void OnError(CameraDevice camera, CameraError error)
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
    public class MainActivity : AppCompatActivity, TextureView.ISurfaceTextureListener, TextToSpeech.IOnInitListener, ILoaderCallbackInterface
    {
        private static Context context;
        public static int cameraFacing;
        public static TextureView textureView;
        private static readonly string FOLDER_NAME = "/CustomVision";
        private static int IMAGE_FOLDER_COUNT = 1;
        public static readonly ImageClassifier imageClassifier = new ImageClassifier();
        public static Android.Util.Size previewSize;
        public static ImageReader imageReader;
        internal static CameraDevice cameraDevice;
        private HandlerThread backgroundThread;
        public static Handler backgroundHandler;
        public static CaptureRequest.Builder captureRequestBuilder;
        internal static CameraCaptureSession cameraCaptureSession;
        public string CameraId { get; private set; }
        private int DSI_height;
        private int DSI_width;
        public static int canProcessImage = 0;
        public static BlockingCollection<BitmapPrefix> bc = new BlockingCollection<BitmapPrefix>();
        private static Task task;
        private static Task task2;
        private static readonly object locker = new object();
        public static bool show_video = false;
        public static string PreviousText = "noLabel";

        private static readonly string[] permissions = {
            Manifest.Permission.WriteExternalStorage,
            Manifest.Permission.Camera
        };
        private static List<string> storeWindow = new List<string>();
        private static TextToSpeech tts;
        private static readonly int WINDOW_SIZE = 5;
       
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            context = ApplicationContext;
            show_video = Intent.GetBooleanExtra("show_video", false);
            cameraFacing = (int)LensFacing.Back;
            string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + 
                FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
            if (show_video)
            {
                textureView = new TextureView(this);
                SetContentView(textureView);
                Window.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
            }

            if (Directory.Exists(sdcardPath))
            {
                while (Directory.Exists(sdcardPath))
                {
                    IMAGE_FOLDER_COUNT += 1;
                    sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + 
                        FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
                }
            }
            if (!Directory.Exists(sdcardPath))
            {
                if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, 
                    Manifest.Permission.WriteExternalStorage) == Permission.Granted)
                {
                    Directory.CreateDirectory(sdcardPath);
                }
                tts = new TextToSpeech(this, this);
            }
        }

        public static void Speak(String CurrentText)
        {
            if (PreviousText == CurrentText)
            {
                tts.Speak(CurrentText, QueueMode.Add, null, null);
            }
            else
            {
                tts.Speak(CurrentText, QueueMode.Flush, null, null);
            }
            PreviousText = CurrentText;
        }

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
            if(show_video && textureView.IsAvailable || !show_video)
            {
                if (cameraDevice == null)
                {
                    CameraManager cameraManager = (CameraManager)GetSystemService(CameraService);
                    SetUpCamera(cameraManager);
                    OpenCamera(cameraManager);
                }
            } else if (show_video && !textureView.IsAvailable)
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
                        //bitmapPrefix.Bitmap.Dispose(); //release the memory to handle OutOfMemory error
                        //**TODO: why is this commented out?
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
                if(show_video)
                {
                    SurfaceTexture surfaceTexture = textureView.SurfaceTexture;
                    surfaceTexture.SetDefaultBufferSize(previewSize.Width, previewSize.Height);
                    Surface previewSurface = new Surface(surfaceTexture);
                    captureRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    captureRequestBuilder.AddTarget(previewSurface);
                    captureRequestBuilder.AddTarget(imageReader.Surface);
                    cameraDevice.CreateCaptureSession(new List<Surface>() { previewSurface,
                        imageReader.Surface }, new CameraCaptureStateCallback(), backgroundHandler);
                } else
                {
                    captureRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    captureRequestBuilder.AddTarget(imageReader.Surface);
                    cameraDevice.CreateCaptureSession(new List<Surface>() { imageReader.Surface }, new CameraCaptureStateCallback(), backgroundHandler);
                }
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

        public static void RecognizeImage(Bitmap rgbBitmap, int prefix)
        {
            if(imageClassifier.RecognizeImage1(rgbBitmap, prefix) == "straight")
            {
                string res = imageClassifier.RecognizeImage2(rgbBitmap, prefix);
                SaveLog("Recognize image", DateTime.Now, prefix);
            }
            else
            {
               SaveLog("Curve Image", DateTime.Now, prefix);
            }
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
                if (count >= 3)
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
                string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + 
                    FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
                string fileName = prefix + ".  " + currentDate.TimeOfDay + ".png";
                string FilePath = System.IO.Path.Combine(sdcardPath, fileName);

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

            Type.Builder yuvType = new Type.Builder(rs, Element.U8(rs)).SetX(data.Length);
            Allocation inAll = Allocation.CreateTyped(rs, yuvType.Create());

            Type.Builder rgbaType = new Type.Builder(rs, Element.RGBA_8888(rs)).SetX(W).SetY(H);
            Allocation outAll = Allocation.CreateTyped(rs, rgbaType.Create());

            Bitmap bmpout = Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888); 
            inAll.CopyFromUnchecked(data);

            yuvToRgbIntrinsic.SetInput(inAll);
            yuvToRgbIntrinsic.ForEach(outAll);
            outAll.CopyTo(bmpout);
            image.Close();
            return bmpout;
        }

        public static void SaveLog(string label, DateTime currentTime, int prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + "_" + label;
            string sdCardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME +
                "/" + IMAGE_FOLDER_COUNT;
            string filePath = System.IO.Path.Combine(sdCardPath, IMAGE_FOLDER_COUNT+"log.txt");
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
            string sdCardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME +
                "/" + IMAGE_FOLDER_COUNT;
            string filePath = System.IO.Path.Combine(sdCardPath,"A_threslog.txt");
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
                        DSI_height = displayMetrics.HeightPixels;
                        DSI_width = displayMetrics.WidthPixels;
                        previewSize = streamConfigurationMap.GetOutputSizes(
                            (int)ImageFormatType.Yuv420888)[0];

                        if (show_video)
                        {
                            SetAspectRatioTextureView(previewSize.Width, previewSize.Height);
                        }

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

        private Mat DetectColor(Mat img)
        {
            
            Mat mask1 = new Mat();
            Mat mask2 = new Mat();
            Mat hsvImg = new Mat();
            Imgproc.CvtColor(img, hsvImg, Imgproc.ColorRgb2hsv,0);
            Core.InRange(hsvImg, new Scalar(0, 50, 20)/*BGRA*/,new Scalar(5,255, 255), mask1);
            Core.InRange(hsvImg, new Scalar(175, 50, 20)/*BGRA*/, new Scalar(180, 255, 255), mask2);
            //Imgproc.CvtColor(mask, mask, Imgproc.ColorHsv2rgb,0);
            //Imgproc.CvtColor(mask, mask, Imgproc.ColorRgb2rgba, 0);

            Mat output = new Mat();
           
            //Core.Bitwise_not(mask, mask);
            Core.Bitwise_or(mask1, mask2, mask1);
            Core.Bitwise_and(img, img, output, mask1);
            mask1.Release();
            mask2.Release();
            hsvImg.Release();
            //Core.Bitwise_not(output, output);
            
            return output;
        }
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
                    if(MainActivity.show_video)
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
                    } else
                    {
                        MainActivity.SaveLog("begin bitmap conversion", DateTime.Now, prefix); // write when the photo can be processed to the log
                        //yuv420888 update line 506 as well
                        bitmap = MainActivity.YUV_420_888_toRGBIntrinsics(image);
                        MainActivity.SaveLog("converted from yuv to bmp", DateTime.Now, prefix); // write when the photo can be processed to the log
                        /*ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                        byte[] bytes = new byte[buffer.Capacity()];
                        buffer.Get(bytes);
                        bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, 
                            null);*/

                        Log.Debug("iowa", "this is the front camera.");
                        int cx = bitmap.Width / 2;
                        int cy = bitmap.Height / 2;
                        Matrix matrix = new Matrix();
                        if (MainActivity.cameraFacing == (int)LensFacing.Front)
                        {
                            matrix.PostScale(-1, 1, cx, cy);
                        }
                        matrix.PostRotate(90);
                        MainActivity.SaveLog("rotated photo", DateTime.Now, prefix); // write when the photo can be processed to the log
                        bitmap = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                        MainActivity.SaveLog("create resized bitmap", DateTime.Now, prefix); // write when the photo can be processed to the log
                    }

                    if (bitmap != null)
                    {
                        //retrieve the input size from the ImageClassifier
                        int inputsize = MainActivity.imageClassifier.getInputSize();

                        //resize the bitmap
                        Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, inputsize, inputsize, false);
                        Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false);

                        Mat imgMat = new Mat(); 
                        Utils.BitmapToMat(resizedBitmap, imgMat);

                        //imgMat = imgMat.Submat(imgMat.Height() - 2 * rgba.Height() / 8, top + height, left, left + width);


                        imgMat = DetectColor(imgMat);

                        
                        Mat cannyMat = new Mat();

                        //Blur and detect edge
                        
                        Size ksize = new Size(5, 5);
                        Imgproc.GaussianBlur(imgMat, imgMat, ksize, 0);
                        Imgproc.Canny(imgMat, cannyMat, 50, 150);

                        //Detec lines from edge image
                        Mat lines = new Mat();
                        int threshold = 50;
                        int minLineSize = 100;
                        int lineGap = 10;
                        Imgproc.HoughLinesP(cannyMat, lines,1, Math.PI / 180, threshold, minLineSize, lineGap);

                        double sumOfAngle = 0.0;
                        for (int x = 0; x < lines.Rows(); x++)
                        {
                            double[] vec = lines.Get(x, 0);
                            double x1 = vec[0],
                                    y1 = vec[1],
                                    x2 = vec[2],
                                    y2 = vec[3];
                            Org.Opencv.Core.Point start = new Org.Opencv.Core.Point(x1, y1);
                            Org.Opencv.Core.Point end = new Org.Opencv.Core.Point(x2, y2);
                            double dx = x1 - x2;
                            double dy = y1 - y2;

                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            double angle = Math.Atan2(dy, dx) * (float)(180 / Math.PI); //measure slope

                            if (dist > 30)  //lines that have length greater than 30
                            {
                                Imgproc.Line(imgMat, start, end, new Scalar(0, 255, 0, 255), 3);
                                sumOfAngle += angle;
                            }

                        }

                        sumOfAngle /= lines.Rows(); //average of slopes
                        int lineNum = lines.Rows();
                        MainActivity.SaveLog_thres(sumOfAngle, DateTime.Now, prefix);
                        //Log.Error("iowa", "angle print");
                        //Console.WriteLine(sumOfAngle);

                        //convert Mat to Bitmap again
                        Utils.MatToBitmap(imgMat, resizedBitmap);

                        //Release all Mats
                        imgMat.Release();
                        cannyMat.Release();
                        lines.Release();
                        Double veerRight_Thres = -50.0; //intial threshold
                        Double veerLeft_Thres = 100.0; //intial threshold
                        Double inlaneMinThres = -50;
                        Double inlaneMaxThres = 100;

                        if (lineNum != 0)
                        {
                            if (sumOfAngle < veerRight_Thres)
                            {
                                MainActivity.Speak("Veerright");
                            }
                            else if (sumOfAngle > veerLeft_Thres)
                            {
                                MainActivity.Speak(" Veerleft");
                            }
                            else if (sumOfAngle > inlaneMinThres && sumOfAngle < inlaneMaxThres)
                            {
                                MainActivity.Speak("Inlane");
                            }
                        }
                        //Utils.MatToBitmap(imgMat, resizedBitmap);
                        MainActivity.SaveLog("created bitmap", DateTime.Now, prefix); // write when the bitmap is created to the log
                        BitmapPrefix bitmapPrefix = new BitmapPrefix(resizedBitmap, prefix); // **TODO
                        if (!MainActivity.bc.IsAddingCompleted) // **TODO
                        {
                            MainActivity.bc.Add(bitmapPrefix); // **TODO
                            //MainActivity.RecognizeImage(resizedBitmap, prefix); // call the classifier to recognize the resizedimage
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


