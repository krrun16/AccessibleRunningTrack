using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.App;
using Android.Graphics;
using Org.Tensorflow.Contrib.Android;
using Plugin.TextToSpeech;

namespace CustomVision
{
    /* This is where the image classification occurs
     */
    public class ImageClassifier
    {
        private readonly List<string> labels1;
        private readonly List<string> labels2;
        private readonly TensorFlowInferenceInterface inferenceInterface1;
        private readonly TensorFlowInferenceInterface inferenceInterface2;
        private static int InputSize;
        private static readonly string InputName = "Placeholder";
        private static readonly string OutputName = "loss";
        private static readonly String[] DATA_NORM_LAYER_PREFIX = {"data_bn", "BatchNorm1"};
        private static Boolean hasNormalizationInterface1 = false;
        private static Boolean hasNormalizationInterface2 = false;
        private static readonly object locker = new object();

        public ImageClassifier()
        {
            Android.Content.Res.AssetManager assets = Application.Context.Assets;
			inferenceInterface1 = new TensorFlowInferenceInterface(assets, "model1.pb");
            inferenceInterface2 = new TensorFlowInferenceInterface(assets, "model2.pb");

            //find the InputSize from the model
            InputSize = (int)inferenceInterface2.GraphOperation(InputName).Output(0).Shape().Size(1);
            hasNormalizationInterface1 = hasNormalization(inferenceInterface1);
            hasNormalizationInterface2 = hasNormalization(inferenceInterface2);
            using (StreamReader sr = new StreamReader(assets.Open("labels1.txt")))
            {
                string content = sr.ReadToEnd();
                labels1 = content.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
            using (StreamReader sr = new StreamReader(assets.Open("labels2.txt")))
            {
                string content = sr.ReadToEnd();
                labels2 = content.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
        }

        public int getInputSize() {
            return InputSize;
        }

        private Boolean hasNormalization(TensorFlowInferenceInterface inferenceInterface)
        {
            Java.Util.IIterator opIter= inferenceInterface.Graph().Operations();
            while (opIter.HasNext)
            {
                Org.Tensorflow.Operation op = (Org.Tensorflow.Operation) opIter.Next();
                for (int i=0; i<DATA_NORM_LAYER_PREFIX.Length;++i)
                {
                    if (op.Name().Contains(DATA_NORM_LAYER_PREFIX[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public string RecognizeImage1(Bitmap bitmap, int prefix)
        {
            string[] outputNames = new[] { OutputName };
            float[] floatValues = GetBitmapPixels(bitmap,prefix,false,hasNormalizationInterface1);
            float[] outputs = new float[labels1.Count];

            inferenceInterface1.Feed(InputName, floatValues, 1, InputSize, InputSize, 3);
            inferenceInterface1.Run(outputNames);
            inferenceInterface1.Fetch(OutputName, outputs);

            List<Tuple<float, string>> results = new List<Tuple<float, string>>();
            for (int i = 0; i < outputs.Length; ++i)
            {
                results.Add(Tuple.Create(outputs[i], labels1[i]));
            }
            IOrderedEnumerable<Tuple<float, string>> orderedResults = 
                results.OrderByDescending(t => t.Item1);
            string orderedResultsMsg = "";
            for (int i = 0; i < orderedResults.Count(); i++)
            {
                orderedResultsMsg += orderedResults.ElementAt(i).Item2 + ": " 
                    + orderedResults.ElementAt(i).Item1 + "; ";
            }
            MainActivity.SaveLog(orderedResultsMsg, DateTime.Now, prefix);
           
                return orderedResults.First().Item2;
        }
        public string RecognizeImage2(Bitmap bitmap, int prefix)
        {
            MainActivity.SaveLog("start method", DateTime.Now, prefix);
            string[] outputNames = new[] { OutputName };
            float[] floatValues = GetBitmapPixels(bitmap,prefix,true, hasNormalizationInterface2);
            MainActivity.SaveLog("got bitmappixels", DateTime.Now, prefix);
            float[] outputs = new float[labels2.Count];

            inferenceInterface2.Feed(InputName, floatValues, 1, InputSize, InputSize, 3);
            MainActivity.SaveLog("feed", DateTime.Now, prefix);
            inferenceInterface2.Run(outputNames);
            MainActivity.SaveLog("run", DateTime.Now, prefix);
            inferenceInterface2.Fetch(OutputName, outputs);
            MainActivity.SaveLog("fetch", DateTime.Now, prefix);

            List<Tuple<float, string>> results = new List<Tuple<float, string>>();
            for (int i = 0; i < outputs.Length; ++i)
            {
                results.Add(Tuple.Create(outputs[i], labels2[i]));
            }
            IOrderedEnumerable<Tuple<float, string>> orderedResults =
                results.OrderByDescending(t => t.Item1);
            string orderedResultsMsg = "";
            for (int i = 0; i < orderedResults.Count(); i++)
            {
                orderedResultsMsg += orderedResults.ElementAt(i).Item2 + ": "
                    + orderedResults.ElementAt(i).Item1 + "; ";
            }
            MainActivity.SaveLog(orderedResultsMsg, DateTime.Now, prefix);
            //CrossTextToSpeech.Current.Speak($"{orderedResults.First().Item2}");
            
            MainActivity.StoreResult(orderedResults.First().Item2);
            string bestResultSoFar = MainActivity.GetTopResult(labels2);
            if (bestResultSoFar == null)
            {
                MainActivity.Speak(orderedResults.First().Item2);
                return orderedResults.First().Item2;
            }
            else
            {
                MainActivity.SaveLog("best result found: " + bestResultSoFar, DateTime.Now, prefix);
                MainActivity.Speak(bestResultSoFar);
                return bestResultSoFar;
            }
        }

        private static float[] GetBitmapPixels(Bitmap bitmap, int prefix, bool saveImage, Boolean hasNormalization)
        {
            float[] floatValues = new float[InputSize * InputSize * 3];
            //using (Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, InputSize, InputSize, false))
            //{
                //using (Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false))
                //{
                    int[] intValues = new int[InputSize * InputSize];
                    bitmap.GetPixels(intValues, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);

                    
                    float IMAGE_MEAN_R = 0;
                    float IMAGE_MEAN_G = 0;
                    float IMAGE_MEAN_B = 0; 

                    if(hasNormalization == false)
                    {
                        IMAGE_MEAN_R = 124;
                        IMAGE_MEAN_G = 117;
                        IMAGE_MEAN_B = 105;
                    }

                    for (int i = 0; i < intValues.Length; ++i)
                    {
                        int val = intValues[i];

                        floatValues[(i * 3) + 0] = (val & 0xFF) - IMAGE_MEAN_B;
                        floatValues[(i * 3) + 1] = ((val >> 8) & 0xFF) - IMAGE_MEAN_G;
                        floatValues[(i * 3) + 2] = ((val >> 16) & 0xFF) - IMAGE_MEAN_R;
                    }
            return floatValues;
        }
    }
}
