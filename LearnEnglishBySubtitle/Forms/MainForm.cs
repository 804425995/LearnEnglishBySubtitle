﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Studyzy.LearnEnglishBySubtitle.EngDict;
using Studyzy.LearnEnglishBySubtitle.Entities;
using Studyzy.LearnEnglishBySubtitle.Helpers;
using Studyzy.LearnEnglishBySubtitle.Subtitles;
using Studyzy.LearnEnglishBySubtitle.TranslateServices;

namespace Studyzy.LearnEnglishBySubtitle.Forms
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            //dbOperator = DbOperator.Instance;
            Global.DictionaryService = new ViconDictionaryService();
            Global.RemoveChinese = true;
            Global.ShortMean = true;
            //englishWordService = new EnglishWordService();
        }

        private SentenceParse sentenceParse=new SentenceParse();
        private TranslateService translateService = new YoudaoTranslateService();
        private ISubtitleOperator stOperator;
        //private int userRank = 4;
        
        //private DbOperator dbOperator;
        private Subtitle subtitle;
        //private EnglishWordService englishWordService;
        
        //private void btnOpenFile_Click(object sender, EventArgs e)
        //{
        //    if (this.openFileDialog1.ShowDialog() == DialogResult.OK)
        //    {
        //        txbSubtitleFilePath.Text = openFileDialog1.FileName;
        //    }
        //}

        private void btnParse_Click(object sender, EventArgs e)
        {
            if (this.openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                txbSubtitleFilePath.Text = openFileDialog1.FileName;
                ReadAndShowSubtitle();
            }
        }

        private void ReadAndShowSubtitle()
        {
            var txt = FileOperationHelper.ReadFile(txbSubtitleFilePath.Text);
            stOperator = SubtitleHelper.GetOperatorByFileName(txbSubtitleFilePath.Text);
            var srts = stOperator.Parse(txt);

            srts = stOperator.RemoveChinese(srts);
            srts = stOperator.RemoveFormat(srts);
            ShowSubtitleText(srts.Bodies.Values);
            subtitle = srts;
        }

        private void ShowSubtitleText(ICollection<SubtitleLine> srts,bool withMean=false )
        {
            dgvSubtitleSentence.Rows.Clear();
            foreach (var subtitleLine in srts)
            {
                //var subtitleLine = kv.Value;
                var txt = subtitleLine.Text;
                if (Global.RemoveChinese)
                {
                    if (withMean)
                    {
                        txt = subtitleLine.EnglishTextWithMeans;
                    }
                    else
                    {
                        txt = subtitleLine.EnglishText;
                    }
                }
                if (!string.IsNullOrEmpty(txt.Trim()))
                {
                    DataGridViewRow row=new DataGridViewRow();
                    row.CreateCells(dgvSubtitleSentence);
                    row.Cells[0].Value = subtitleLine.Number.ToString();

                    string timeLine = subtitleLine.StartTime.ToString("HH:mm:ss") + "->" + subtitleLine.EndTime.ToString("HH:mm:ss");

                    row.Cells[1].Value = timeLine;
                    row.Cells[2].Value = txt;
                    dgvSubtitleSentence.Rows.Add(row);
                }
            }
        }

      
     
        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadConfig();
            backgroundLoadDictionary.RunWorkerAsync();
            PronunciationDownloader.DownloadPronunciation();
        }
        private void ShowMessage(string message)
        {
            toolStripStatusLabel1.Text = message;
        }


        private void btnRemark_Click(object sender, EventArgs e)
        {
            if (subtitle == null || subtitle.Bodies.Count == 0)
            {
                MessageBox.Show("请先点击“载入字幕”按钮打开字幕文件");
                return;
            }
        
            Splash.Show("解析字幕中...");
          
            sentenceParse = new SentenceParse();
            var subtitleWords = PickNewWords(subtitle.Bodies.Values);
            if (subtitleWords.Count > 0)
            {
                NewWordConfirmForm form = new NewWordConfirmForm();
                form.DataSource = subtitleWords.Values.ToList();
                form.SubtitleFileName = Path.GetFileName(txbSubtitleFilePath.Text);
                form.OnClickOkButton += RemarkSubtitle;
                form.Show();
                form.Activate();
                //if (form.ShowDialog() == DialogResult.OK)
                //{

                //  form.SelectedNewWords
                //}
            }
            Splash.Close();

        }
        /// <summary>
        /// 在保存用户认识和不认识的词后将不认识的词传回来，对字幕进行注释
        /// </summary>
        /// <param name="words">不认识的词</param>
        private void RemarkSubtitle(IList<SubtitleWord>  words)
        {
            Dictionary<string, SubtitleWord> result = new Dictionary<string, SubtitleWord>();
            foreach (var subtitleWord in words)
            {
                foreach (var wordInSubtitle in subtitleWord.WordInSubtitle)
                {
                    result.Add(wordInSubtitle, subtitleWord);
                }
               
                //var formats = EnglishWordService.GetWordAllFormat(subtitleWord.Word);
                //foreach (var wordFormat in formats)
                //{
                //    if (!result.ContainsKey(wordFormat))
                //    {
                //        result.Add(wordFormat,subtitleWord);
                //    }
                //}
            }
            var newSubtitle = new List<SubtitleLine>();
            for (int i = 1; i <= subtitle.Bodies.Count; i++)
            {
                var subtitleLine = subtitle.Bodies[i];
                subtitleLine.EnglishTextWithMeans = ReplaceSubtitleLineByVocabulary(subtitleLine.EnglishText, result);
                newSubtitle.Add(subtitleLine);
            }
            //subtitle.Bodies = newSubtitle;
            ShowSubtitleText(newSubtitle, true);
            //ClearCache();
        }


        /// <summary>
        /// 找到字幕中的生词，先进行分词，然后取每个单词的原型，然后看每个单词是否认识，认识则跳过，不认识则注释。
        /// </summary>
        /// <param name="subtitles"></param>
        /// <returns></returns>
        private IDictionary<string, SubtitleWord> PickNewWords(ICollection<SubtitleLine> subtitles)
        {
            Dictionary<string, SubtitleWord> result = new Dictionary<string, SubtitleWord>();
            var unknownWords = DbOperator.Instance.GetAllUserUnKnownVocabulary().ToDictionary(s=>s.Word,s=>s.IsStar);
            var texts = subtitles.Select(s => s.EnglishText).ToList();
           
            foreach (var line in texts)
            {
                var lineResult = sentenceParse.Pickup(line);
                foreach (KeyValuePair<string, string> keyValuePair in lineResult)
                {
                    string original = keyValuePair.Key;
                    //if (knownWords.Contains(original)) continue;
                    string word = keyValuePair.Value;
                  
                    //if(knownWords.Contains(word)) continue;
                    if (result.ContainsKey(original))
                    {
                        result[original].ShowCount++;
                        if (!result[original].WordInSubtitle.Contains(word))
                        {
                            result[original].WordInSubtitle.Add(word);
                        }
                        continue;
                    }
                 
                    var mean = sentenceParse.RemarkWord(line, word, original);
                    if (mean != null)
                    {
                        var wd = new SubtitleWord()
                        {
                            Word = mean.Word,
                            ShowCount = 1,
                            WordInSubtitle = new List<string>() {word},
                            Means = mean.Means,
                            SubtitleSentence = line,
                            SelectMean = mean.DefaultMean == null ? mean.Means[0].ToString() : mean.DefaultMean.ToString()
                        };
                        if (unknownWords.ContainsKey(mean.Word))
                        {
                            wd.IsStar = unknownWords[mean.Word];
                        }
                        result.Add(original, wd);
                    }
                }
                
            }
            return result;
        }
        /// <summary>
        /// 将一个句子中的生词进行注释
        /// </summary>
        /// <param name="line"></param>
        /// <param name="words"></param>
        /// <returns></returns>
        private string ReplaceSubtitleLineByVocabulary(string line,IDictionary<string,SubtitleWord> words )
        {

            StringBuilder sb = new StringBuilder();
            foreach (string s in SentenceParse.SplitSentence(line))
            {
                if (s.Length == 1)
                {
                    sb.Append(s);
                }
                else
                {
                    var word = s.ToLower();
                    string mean = "";
                    SubtitleWord wordWithMean = null;
                    if (words.ContainsKey(s))//这个词需要注释
                    {
                        wordWithMean = words[s];
                    }
                    else if (words.ContainsKey(word))
                    {
                        wordWithMean = words[word];
                    }
                    mean = wordWithMean?.SelectMean;
                    if (!String.IsNullOrEmpty(mean))
                    {
                        if (Global.ShortMean)
                        {
                            var meanarray = mean.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            mean = meanarray[0];
                            mean = mean.Substring(mean.IndexOf(' ') + 1);
                        }
                        var formatted = string.Format("{0}({1})", s, mean.Trim());
                        if (wordWithMean.IsStar)
                        {
                            //标星的单词，需要突出显示
                            formatted = String.Format("<font color='{0}'>{1}</font>", ColorTranslator.ToHtml(meanColor), formatted);
                        }
                        sb.Append(formatted);
                    }
                    else
                    {
                        sb.Append(s);
                    }
                }
            }
            return sb.ToString();

            //var array = SentenceParse.SplitSentence2Words(line);
            //foreach (string oword in array)
            //{
            //    var word = oword.ToLower();
              
            //    //var w = englishWordService.GetOriginalWord(word);
            //    //var mean = words.ContainsKey(w) ? words[w].SelectMean : "";
            //    //if (!string.IsNullOrEmpty(mean))
            //    //{
            //    //    if(shortMean)
            //    //    {
            //    //        var meanarray = mean.Split(new char[]{';',','},StringSplitOptions.RemoveEmptyEntries);
            //    //        mean = meanarray[0];
            //    //    }
            //    //    line = ReplaceNewWord(line, word, mean);
            //    //}
            //}
            //return line;
        }




        //private Dictionary<string, EngDictionary> cachedDict = new Dictionary<string, EngDictionary>();

        

        //private IList<UserVocabulary> userVocabularies;
 
        //private UserVocabulary GetUserVocabulary(string word)
        //{
        //    if (userVocabularies == null||userVocabularies.Count==0)
        //    {
        //        userVocabularies = dbOperator.GetAllUserVocabulary();
        //    }
        //    return (userVocabularies.Where(v => v.Word == word).FirstOrDefault());
        //}

        //private IList<VocabularyRank> vocabularyRanks;
        //private VocabularyRank GetVocabularyRank(string word)
        //{
        //  if (vocabularyRanks == null||vocabularyRanks.Count==0)
        //    {
        //        vocabularyRanks = dbOperator.GetAll<VocabularyRank>();
        //    }
        //    return (vocabularyRanks.Where(v => v.Word == word).FirstOrDefault());
        //}
        //private string CalcAndGetWordAndMean(string word)
        //{

        //    var vocabulary = GetUserVocabulary(word);
        //    if (vocabulary != null)
        //    {
        //        if (vocabulary.KnownStatus == KnownStatus.Known)
        //        {
        //            //用户认识这个词，那么就不用替换
        //            return "";
        //        }
        //        else //用户的生词表中有这个词，
        //        {
        //            return (word);
        //        }
        //    }
        //    //用户词汇中没有这个词，那么就查询词频分级表，看有没有分级信息
        //    var rankData = InnerDictionaryHelper.GetAllVocabularyRanks();
        //    //var rank = GetVocabularyRank(word);
        //    if (!rankData.ContainsKey(word))
        //    {
        //        return word;
        //    }
        //    var rank = rankData[word];


        //    if (rank < 4)
        //    {
        //        return (word);
        //    }
        //    else
        //    {
        //        return "";
        //    }
        //}




        private void backgroundLoadDictionary_DoWork(object sender, DoWorkEventArgs e)
        {
            Global.DictionaryService.IsInDictionary("a");
          
            sentenceParse.RemarkWord("Test it.", "it", "it");
        }

        private void backgroundLoadDictionary_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ShowMessage("字典装载完成.");
            btnRemark.Enabled = true;
        }
        //private  void ClearCache()
        //{
        //    userVocabularies.Clear();
        //    cachedDict.Clear();
        //}

        private void btnSave_Click(object sender, EventArgs e)
        {
         
            var nsubtitle = BuildSubtitleFromGrid();
            if (nsubtitle == null || nsubtitle.Bodies.Count == 0)
            {
                MessageBox.Show("请先点击“载入字幕”按钮打开字幕文件");
                return;
            }
            //if (meanColor != default(Color))
            //{
            //    Regex r=new Regex(@"\(([^\)]+)\)");
            //    var fontFormat = "(<font color='#" + meanColor.R.ToString("x2") + meanColor.G.ToString("x2") +
            //                 meanColor.B.ToString("x2") + "'>$1</font>)";
            //    foreach (var kv in nsubtitle.Bodies)
            //    {
            //        var line = kv.Value;
            //        if (r.IsMatch(line.Text))
            //        {
            //            line.Text = r.Replace(line.Text, fontFormat);
            //        }
            //    }

              
            //}
            var path = txbSubtitleFilePath.Text;
            var newFile = Path.GetDirectoryName(path)+"\\" +Path.GetFileNameWithoutExtension(path)+ "_new"+Path.GetExtension(path);
            //if(!File.Exists(newFile))
            //{
            //    File.Copy(txbSubtitleFilePath.Text, newFile);
            //}
            var str = stOperator.Subtitle2String(nsubtitle);
            FileOperationHelper.WriteFile(newFile, Encoding.UTF8, str);
            ShowMessage("保存成功");
            MessageBox.Show("保存成功,文件名："+Path.GetFileName(newFile));
        }

        private Subtitle BuildSubtitleFromGrid()
        {
           Subtitle nsubtitle=new Subtitle();
            nsubtitle.Header = subtitle.Header;
            nsubtitle.Footer = nsubtitle.Footer;
            nsubtitle.Bodies = new Dictionary<int, SubtitleLine>();
            foreach (DataGridViewRow row in dgvSubtitleSentence.Rows)
            {
                SubtitleLine line=new SubtitleLine();
                var number = Convert.ToInt32( row.Cells[0].Value.ToString());
                var orgLine = subtitle.Bodies[number];//.SingleOrDefault(i => i.Number == number);
                line.Number = number;
                line.StartTime = orgLine.StartTime;
                line.EndTime = orgLine.EndTime;
                line.Text = row.Cells[2].Value.ToString();
                line.OriginalText = orgLine.OriginalText;
                nsubtitle.Bodies.Add(number,line);
            }
            return nsubtitle;
        }

        //private void ToolStripMenuItemAdjustSubtitleTimeline_Click(object sender, EventArgs e)
        //{
        //    TimelineAdjustForm form=new TimelineAdjustForm();
        //    form.Show();
        //}

        #region Menu

        private void ToolStripMenuItemFilterChinese_Click(object sender, EventArgs e)
        {
            Global.RemoveChinese = ToolStripMenuItemFilterChinese.Checked;
        }

        private void ToolStripMenuItemShortMean_Click(object sender, EventArgs e)
        {
            Global.ShortMean = ToolStripMenuItemShortMean.Checked;
        }

        private void ToolStripMenuItemMeanStyleConfig_Click(object sender, EventArgs e)
        {
            var r = colorDialog1.ShowDialog();
            if ( r== DialogResult.OK)
            {
                meanColor = colorDialog1.Color;
            }
            else if(r==DialogResult.Cancel)
            {
                meanColor = default(Color);
            }
        }

        private Color meanColor=Color.Red;

        private void ToolStripMenuItemDictionaryConfig_Click(object sender, EventArgs e)
        {
            //DictionaryConfigForm form=new DictionaryConfigForm();
            //if (form.ShowDialog() == DialogResult.OK)
            //{
            //    this.dictionaryService = form.SelectDictionaryService;
            //    englishWordService.DictionaryService = dictionaryService;
            //    if(!backgroundLoadDictionary.IsBusy)
            //    {
            //        backgroundLoadDictionary.RunWorkerAsync();
            //    }
            //}
        }

        private void ToolStripMenuItemUserVocabularyConfig_Click(object sender, EventArgs e)
        {
            UserVocabularyConfigForm form=new UserVocabularyConfigForm();
            form.Show();
        }

        private void ToolStripMenuItemUserVocabularyMgt_Click(object sender, EventArgs e)
        {
            UserVocabularyMgtForm form=new UserVocabularyMgtForm();
            form.Show();
        }

        private void ToolStripMenuItemAbount_Click(object sender, EventArgs e)
        {
            AboutBox a=new AboutBox();
            a.Show();
        }

        private void ToolStripMenuItemDonate_Click(object sender, EventArgs e)
        {
           
            DonationForm donation=new DonationForm();
            donation.Show();
            donation.Activate();
        }

        private void ToolStripMenuItemLastVersion_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/studyzy/LearnEnglishBySubtitle/releases");
        }

       

        private void ToolStripMenuItemHelp_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/studyzy/LearnEnglishBySubtitle/wiki");
        }

     
        private void YoudaoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            YoudaoToolStripMenuItem.Checked = true;
            BaiduToolStripMenuItem.Checked = false;
            MicrosoftToolStripMenuItem.Checked = false;
            GoogleToolStripMenuItem.Checked = false;
            translateService=new YoudaoTranslateService();
        }

        private void BaiduToolStripMenuItem_Click(object sender, EventArgs e)
        {
            YoudaoToolStripMenuItem.Checked = false;
            BaiduToolStripMenuItem.Checked = true;
            MicrosoftToolStripMenuItem.Checked = false;
            GoogleToolStripMenuItem.Checked = false;
            translateService = new BaiduTranslateService();
        }

        private void MicrosoftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            YoudaoToolStripMenuItem.Checked = false;
            BaiduToolStripMenuItem.Checked = false;
            MicrosoftToolStripMenuItem.Checked = true;
            GoogleToolStripMenuItem.Checked = false;
            translateService = new MsTranslateService();
        }

        private void GoogleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            YoudaoToolStripMenuItem.Checked = false;
            BaiduToolStripMenuItem.Checked = false;
            MicrosoftToolStripMenuItem.Checked = false;
            GoogleToolStripMenuItem.Checked = true;
            translateService = new GoogleTranslateService();
        }

        #endregion

        #region Drag
        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Link;
            else e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            var array = (Array)e.Data.GetData(DataFormats.FileDrop);
            string files = "";


            foreach (object a in array)
            {
                string path = a.ToString();
                files += path + " | ";
            }
            txbSubtitleFilePath.Text = files.Remove(files.Length - 3);
            ReadAndShowSubtitle();
        }

        #endregion

        private void dgvSubtitleSentence_Resize(object sender, EventArgs e)
        {
            dgvSubtitleSentence.Columns[2].Width = dgvSubtitleSentence.Width - 179;
        }

        private void SentenceTranslateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dgvSubtitleSentence.SelectedCells.Count > 0)
            {
                var cell = dgvSubtitleSentence.SelectedCells[0];
                if (cell.ColumnIndex == 2)//只对字幕句子进行翻译
                {
                    var sentence = cell.Value.ToString();
                    try
                    {
                        cell.Value = sentence + "\r\n" + translateService.TranslateToChinese(sentence);
                    }
                    catch (Exception ex)
                    {
                        
                        MessageBox.Show("整句翻译服务调用失败，请尝试其他服务");
                    }
                }
            }
        }

        private void PronunciationSetupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PronunciationForm form=new PronunciationForm();
            if (form.ShowDialog() == DialogResult.OK)
            {
                LoadConfig();
            }
        }


        private void LoadConfig()
        {
            Global.PronunciationType = DbOperator.Instance.GetConfigValue("PronunciationType");
            Global.PronunciationDownload = Convert.ToBoolean(DbOperator.Instance.GetConfigValue("PronunciationDownload"));

        }

        private void PreviewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PreviewVocabularyForm form=new PreviewVocabularyForm();
            form.Show();
            form.Activate();
        }

    }
}
