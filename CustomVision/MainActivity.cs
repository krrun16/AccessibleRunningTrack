using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Util;
using System.IO;
using Android.Support.V4.App;
using Android;
using Android.Hardware.Camera2;
using Android.Graphics;
using System.Collections.Generic;
using Android.Media;
using System.Threading;
using System.Collections.Concurrent;

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
    public class MainActivity : AppCompatActivity, TextureView.ISurfaceTextureListener
    {
        private int cameraFacing;
        public static TextureView textureView;
        private static readonly string FOLDER_NAME = "/CustomVision";
        private static int IMAGE_FOLDER_COUNT = 1;
        private static readonly ImageClassifier imageClassifier = new ImageClassifier();
        public static Size previewSize;
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

        private static readonly string[] permissions = {
            Manifest.Permission.WriteExternalStorage,
            Manifest.Permission.Camera
        };

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            cameraFacing = (int)LensFacing.Back;
            textureView = new TextureView(this);
            SetContentView(textureView);
            Window.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
            string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;

            if (Directory.Exists(sdcardPath))
            {
                while (Directory.Exists(sdcardPath))
                {
                    IMAGE_FOLDER_COUNT += 1;
                    sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
                }
            }
            if (!Directory.Exists(sdcardPath))
            {
                if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage)
                        == Permission.Granted)
                {
                    Directory.CreateDirectory(sdcardPath);
                }
            }

        }

        protected override void OnResume()
        {
            base.OnResume();
            
            OpenBackgroundThread();
            if(textureView.IsAvailable)
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
            Action action = () =>
            {
                try
                {
                    while (true)
                    {
                        BitmapPrefix bitmapPrefix = bc.Take();
                        MemoryStream byteArrayOutputStream = new MemoryStream();
                        Bitmap cropped = Bitmap.CreateBitmap(bitmapPrefix.Bitmap, 0, 0,
                            textureView.Width, textureView.Height);
                        cropped.Compress(Bitmap.CompressFormat.Png, 100,
                            byteArrayOutputStream);
                        SaveLog("created png", DateTime.Now, bitmapPrefix.Prefix);
                        byte[] png = byteArrayOutputStream.ToArray();
                        SaveBitmap(png, bitmapPrefix.Prefix);
                    }
                }
                catch (InvalidOperationException)
                {

                }

            };

            task = Task.Factory.StartNew(action);
            task2 = Task.Factory.StartNew(action);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CloseCamera();
            CloseBackgroundThread();
            FinishAffinity();
            System.Environment.Exit(0);
        }

        protected override void OnStop()
        {
            base.OnStop();
            CloseCamera();
            CloseBackgroundThread();
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
                cameraDevice.CreateCaptureSession(new List<Surface>() { previewSurface, imageReader.Surface },
                    new CameraCaptureStateCallback(), backgroundHandler);
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
            //string result = await Task.Run(() => imageClassifier.RecognizeImage(rgbBitmap));
            //string result = imageClassifier.RecognizeImage1(rgbBitmap, prefix);
            if(imageClassifier.RecognizeImage1(rgbBitmap, prefix) == "straight")
            {
                
                string res = imageClassifier.RecognizeImage2(rgbBitmap, prefix);
                SaveLog("Recognize image", DateTime.Now, prefix);

            }
            else
            {
               // SaveLog("Curve Image", DateTime.Now, prefix);
            }
        }

        private static void SaveBitmap(byte[] data, int prefix) {
            DateTime currentDate = DateTime.Now;
            long ts = currentDate.Ticks;
            string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path+FOLDER_NAME+"/"+IMAGE_FOLDER_COUNT;
            string fileName = prefix + ".  " + currentDate.TimeOfDay + ".png";
            string FilePath = System.IO.Path.Combine(sdcardPath, fileName);

            if (!File.Exists(FilePath))
            {
                File.WriteAllBytes(FilePath, data);
                SaveLog("saved image", DateTime.Now, prefix);
            }
        }

        public static void SaveLog(string label, DateTime currentTime, int prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + "_" + label;
            string sdCardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
            string filePath = System.IO.Path.Combine(sdCardPath, "log.txt");
            lock (locker)
            {
                if (!File.Exists(filePath))
                {
                    using (StreamWriter write = new StreamWriter(filePath, true))
                    {
                        write.Write(msg + "\n");
                    }

                }
                else
                {
                    using (StreamWriter write = new StreamWriter(filePath, true))
                    {
                        write.Write(msg + "\n");
                    }
                }
            }
        }

        private void SetUpCamera(CameraManager cameraManager)
        {
            try {
                foreach (string cameraId in cameraManager.GetCameraIdList()) {
                    CameraCharacteristics cameraCharacteristics =
                            cameraManager.GetCameraCharacteristics(cameraId);
                    if ((int)cameraCharacteristics.Get(CameraCharacteristics.LensFacing) == cameraFacing) {
                        Android.Hardware.Camera2.Params.StreamConfigurationMap streamConfigurationMap =
                            (Android.Hardware.Camera2.Params.StreamConfigurationMap)cameraCharacteristics.Get(
                                CameraCharacteristics.ScalerStreamConfigurationMap);
                        DisplayMetrics displayMetrics = Resources.DisplayMetrics;
                        DSI_height = displayMetrics.HeightPixels;
                        DSI_width = displayMetrics.WidthPixels;
                        previewSize = streamConfigurationMap.GetOutputSizes((int)ImageFormatType.Jpeg)[0];
                        SetAspectRatioTextureView(previewSize.Width, previewSize.Height);
                        imageReader = ImageReader.NewInstance(previewSize.Width, previewSize.Height, ImageFormatType.Yuv420888, 1);
                        imageReader.SetOnImageAvailableListener(new ImageAvailableListener(), backgroundHandler);
                        CameraId = cameraId;
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
                    //textureview is the region of the screen that contains the camera, so get the picture with the same dimensions as the screen
                    Bitmap bitmap = MainActivity.textureView.GetBitmap(MainActivity.textureView.Width, MainActivity.textureView.Height);
                    MainActivity.SaveLog("created bitmap", DateTime.Now, prefix); // write when the bitmap is created to the log
                    image.Close(); // This closes the image so the phone no longer has to hold onto it, otherwise it will slow the system and stop collecting pictures
                    BitmapPrefix bitmapPrefix = new BitmapPrefix(bitmap, prefix); // **TODO
                    MainActivity.bc.Add(bitmapPrefix); // **TODO
                    MainActivity.RecognizeImage(bitmap, prefix); // call the classifier to recognize the image
                }
                Interlocked.Exchange(ref MainActivity.canProcessImage, 1); // equivalent to canProcessImage = 1; meaning anyone else can come with their image
            }
        }
    }
}


