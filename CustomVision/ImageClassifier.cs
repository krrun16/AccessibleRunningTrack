using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.App;
using Android.Graphics;
using Org.Tensorflow.Contrib.Android;

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
        private static readonly int InputSize = 227;
        private static readonly string InputName = "Placeholder";
        private static readonly string OutputName = "loss";

        public ImageClassifier()
        {
            Android.Content.Res.AssetManager assets = Application.Context.Assets;
			inferenceInterface1 = new TensorFlowInferenceInterface(assets, "model1.pb");
            inferenceInterface2 = new TensorFlowInferenceInterface(assets, "model2.pb");

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

        public string RecognizeImage1(Bitmap bitmap, int prefix)
        {
            string[] outputNames = new[] { OutputName };
            float[] floatValues = GetBitmapPixels(bitmap);
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
            if (orderedResults.First().Item1 > .8)
            {
                return orderedResults.First().Item2;
            } else
            {
                return "unknown";
            }
            
        }

        public string RecognizeImage2(Bitmap bitmap, int prefix)
        {
            string[] outputNames = new[] { OutputName };
            float[] floatValues = GetBitmapPixels(bitmap);
            float[] outputs = new float[labels2.Count];

            inferenceInterface2.Feed(InputName, floatValues, 1, InputSize, InputSize, 3);
            inferenceInterface2.Run(outputNames);
            inferenceInterface2.Fetch(OutputName, outputs);

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
            if (orderedResults.First().Item1 > .8)
            {
                // return orderedResults.First().Item2;
                string bestResultSoFar = MainActivity.GetTopResult(labels2);
                MainActivity.StoreResult(orderedResults.First().Item2);
                if (bestResultSoFar == null)
                {
                    return orderedResults.First().Item2;
                }
                else
                {
                    MainActivity.SaveLog("best result found: " + bestResultSoFar, DateTime.Now, prefix);
                    return bestResultSoFar;
                }
            }
            else
            {
                return "unknown";
            }

        }

        private static float[] GetBitmapPixels(Bitmap bitmap)
        {
            float[] floatValues = new float[InputSize * InputSize * 3];

            using (Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, InputSize, InputSize, false))
            {
                using (Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false))
                {
                    int[] intValues = new int[InputSize * InputSize];
                    resizedBitmap.GetPixels(intValues, 0, resizedBitmap.Width, 0, 0, resizedBitmap.Width, resizedBitmap.Height);

                    for (int i = 0; i < intValues.Length; ++i)
                    {
                        int val = intValues[i];

                        floatValues[(i * 3) + 0] = (val & 0xFF) - 104;
                        floatValues[(i * 3) + 1] = ((val >> 8) & 0xFF) - 117;
                        floatValues[(i * 3) + 2] = ((val >> 16) & 0xFF) - 123;
                    }

                    resizedBitmap.Recycle();
                }

                scaledBitmap.Recycle();
            }

            return floatValues;
        }
    }
}
