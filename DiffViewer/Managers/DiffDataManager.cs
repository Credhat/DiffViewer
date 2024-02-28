﻿using DiffPlex;
using DiffPlex.Model;
using DiffViewer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using static VSTSDataProvider.Models.ExecuteVSTSModel;
using static VSTSDataProvider.Models.QueryVSTSModel;

namespace DiffViewer.Managers;

public class DiffDataManager
{
    #region Fields

    const char m_SplitChar = '#';
    const char m_LineChar = '@';
    const char m_UnitOpChar = '^';
    const char m_PlusChar = '+';
    const char m_MinusChar = '-';

    const string m_Completedin = " completed in ";
    const string m_FailedtoCompletein = " failed to complete in ";
    const string m_Identical = "are identical";
    const string m_PostProcessing = "Post Processing...";
    const string m_FailedtoComplete = " failed to complete.";

    const string m_ValidationResults = "Validation Results";
    const string m_EndofSummary = "End of Summary";
    private bool m_IsContainValidation;
    private bool m_IsContainEndofSummary;

    private string _diffFilePath;
    private string? _diffAllNames;
    private List<string> _diffNames = new();
    private List<string> _diffResults = new();

    #endregion Fields



    #region Properties

    /// <summary>
    /// All Diff TestCase with properties.
    /// TestCase Properties: Name, IsIdentical, Raw, OldText_BaseLine, NewText_Actual, MoreInfo.
    /// </summary>
    public List<DiffTestCase>? TestCases { get; private set; }

    /// <summary>
    /// Get Diff Text LineCount and full Content by using FileManager.GetTextInfoAsync().
    /// </summary>
    public (int, string?) DiffInfos => FileManager.GetTextInfoAsync(_diffFilePath).Result;

    /// <summary>
    /// Check whether the Diff Process is over.
    /// If over, return true.
    /// not over, return false.
    /// </summary>
    public bool IsProcessOver { get; private set; }

    #endregion Properties


    /// <summary>
    /// Init DiffDataManager with diffFilePath.
    /// </summary>
    /// <param name="diffFilePath"></param>
    public DiffDataManager(string diffFilePath)
    {
        FileManager.CheckFileExists(diffFilePath , true);
        _diffFilePath = diffFilePath;
    }

    /// <summary>
    /// Handle diff file to get the Diff Compare Results and Diff TestCase Names.
    /// </summary>
    /// <returns></returns>
    public async Task<DiffDataManager> HandleDiff( )
    {
        var results = await LoadAndProcessDiffFileAsync(_diffFilePath);

        _diffResults = results.Item1;
        _diffAllNames = results.Item2;

#if DEBUG
        _diffNames = await ExtractTestCasesNameAsync(_diffAllNames);
        TestCases = _diffNames.CreateTCswithName();
        TestCases = await ExtractTestCaseDiffResultAsync(_diffResults , TestCases);
#endif

#if !DEBUG
        TestCases = (await ExtractTestCasesNameAsync(_diffAllNames)).CreateTCswithName();
        TestCases = await ExtractTestCaseDiffResultAsync(_diffResults , TestCases);
#endif
        return this;
    }



    /// <summary>
    /// Extract the Diff Compare Result for each TestCase.
    /// Update the TestCase Diff Result.
    /// Name, Raw, OldText, NewText, IsIdentical
    /// </summary>
    /// <param name="diffTCResultList"></param>
    /// <param name="testCases"></param>
    /// <returns></returns>
    private async Task<List<DiffTestCase>> ExtractTestCaseDiffResultAsync(List<string> diffTCResultList , List<DiffTestCase> testCases)
    {
        CheckProcessOver();
        var location = $"{nameof(DiffDataManager)}.{nameof(ExtractTestCaseDiffResultAsync)}";

        return await TasksManager.RunTaskWithReturnAsync(( ) =>
        {
            List<DiffTestCase> m_testCases = testCases;

            diffTCResultList.ForEach((s) =>
            {
                string[] strings = s.Split('\n' , StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string? name = null;

                StringBuilder sb = new StringBuilder();

                for( int i = 0; i < strings.Length; i++ )
                {
                    if( strings[i].StartsWith(m_SplitChar) && strings[i].EndsWith(m_SplitChar) )
                    {
                        continue;
                    }
                    else if( name is null && (strings[i].StartsWith(m_PostProcessing) || strings[i].Contains(m_PostProcessing)) )
                    {
                        name = strings[i].Replace(m_PostProcessing , string.Empty).Trim();
                        continue;
                    }else if( name is null && (strings[i].EndsWith(m_FailedtoComplete) || strings[i].Contains(m_FailedtoComplete)) )
                    {
                        name = strings[i].Replace(m_FailedtoComplete, string.Empty).Trim();
                        continue;
                    }
                    sb.AppendLine(strings[i]);
                }

                var raw = sb.ToString();

                var location = $"{nameof(DiffDataManager)}.{nameof(ExtractTestCaseDiffResultAsync)}.{nameof(SplitResultToDiffString)}.{name}";

                var splitData = TasksManager.RunTaskWithReturn(( ) =>
                {
                    return SplitResultToDiffString(raw);
                } , location , catchException: true , throwException: false);

                try
                {

                    // Update the TestCase some major Infos.
                    m_testCases.First(t => (t.Name.Equals(name)))
                               .SetRaw(raw)
                               .SetRawSize(raw)
                               .SetNewText(splitData.actualText)
                               .SetOldText(splitData.baseLineText)
                               .SetIdentical(raw);

                }
                catch( Exception ex )
                {
                    App.Logger.Error($"Current handling TestCase: {name}" +
                                     $"{Environment.NewLine}Exception: {ex.Message}");
                    //throw;
                }

            });
            return m_testCases;
        } , location , catchException: true , throwException: false);

    }

    /// <summary>
    /// Extract the TestCases Name from the Diff Names String.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<List<string>> ExtractTestCasesNameAsync(string diffNamesString)
    {
        string? location = $"{nameof(DiffDataManager)}.{nameof(ExtractTestCasesNameAsync)}";

        return await TasksManager.RunTaskWithReturnAsync(( ) =>
        {
            CheckProcessOver();
            List<string> testCaselist = new List<string>();
            string[] strings = diffNamesString?.Split('\n' , StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? throw new Exception();

            for( int i = 0; i < strings.Length; i++ )
            {
                if( strings[i].Contains(m_Completedin))
                {
                    testCaselist.Add(strings[i].Substring(0 , strings[i].IndexOf(m_Completedin , StringComparison.OrdinalIgnoreCase)));
                }
                else if (strings[i].Contains(m_FailedtoCompletein))
                {
                    testCaselist.Add(strings[i].Substring(0, strings[i].IndexOf(m_FailedtoCompletein, StringComparison.OrdinalIgnoreCase)));
                }
            }

            return testCaselist;

        } , location , catchException: true);

    }

    /// <summary>
    /// Load large Txt file by using FileStream and StreamReader.
    /// And using StringBuilder to concat strings.
    /// </summary>
    /// <param name="diffFilePath"></param>
    private async Task<(List<string>, string?)> LoadAndProcessDiffFileAsync(string diffFilePath)
    {
        App.Logger.Information($"Start handling Diff File: {diffFilePath}");

        string? location = $"{nameof(DiffDataManager)}.{nameof(HandleDiff)}";

        List<string> mDiffResults = new();
        string? mDiffAllNames = string.Empty;

        await TasksManager.RunTaskAsync(( ) =>
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                int bufferSize = 4096;
                int numPostProcessing = 0;
                int numFailedToComplete = 0;
                using( FileStream fs = new FileStream(diffFilePath , FileMode.Open , FileAccess.Read , FileShare.Read , bufferSize , useAsync: true) )
                {
                    using( StreamReader sr = new StreamReader(fs , Encoding.UTF8 , true , bufferSize / 4) )
                    {
                        while( !sr.EndOfStream )
                        {
                            string line = sr.ReadLine();

                            sb.AppendLine(line);

                            // Get the TestCases Name Content.
                            if( !m_IsContainEndofSummary )
                            {
                                if( !m_IsContainValidation )
                                {
                                    if( line.Contains(m_ValidationResults) ) { m_IsContainValidation = true; }
                                }

                                if( line.Contains(m_EndofSummary) )
                                {
                                    if( !m_IsContainValidation ) throw new Exception($"The {m_ValidationResults} have not been found! File Struct is not right!!!");

                                    mDiffAllNames = sb.ToString();
                                    App.Logger.Information($"The {m_ValidationResults} have been found! Diff Name Content got.");
                                    m_IsContainEndofSummary = true;
                                    // Clear Content of StringBuilder.
                                    sb.Clear();
                                }
                            }

                            // Get the Diff Content for each TestCase.
                            // 1. Get the first Post Processing and identity the start of numPostProcessing
                            // 2. Find the next m_PostProcessing, and remove the redundant content which append with the last string builder.
                            // 3. Store the string builder content as the last Test Case diff result and empty sb to append the new Test case diff result.
                            // For instance: 
                            // Last string builder (Test Case diff result):
                            // Post Processing...3phase1
                            // #######################################################
                            //
                            // Results... 
                            // Files v:\hytest\results\dyn_v15media302\3phase1.dmp and V:\HyTest\suites\20_dynamics\standard\3phase1.dmp are identical
                            //
                            //
                            // #######################################################

                            if ( m_IsContainEndofSummary && m_IsContainValidation )
                            {
                                bool PostProcessingMatched = line.StartsWith(m_PostProcessing) || line.Contains(m_PostProcessing);
                                bool FailedtoCompleteMatched = line.EndsWith(m_FailedtoComplete) || line.Contains(m_FailedtoComplete);

                                if ( numPostProcessing == 1 && (PostProcessingMatched || FailedtoCompleteMatched) )
                                {
                                    sb.Remove(sb.Length - line.Length - 2 , line.Length);
                                    mDiffResults.Add(sb.ToString());
                                    sb.Clear();
                                    sb.AppendLine(line);
                                }
                                else if (numFailedToComplete == 1 && (PostProcessingMatched || FailedtoCompleteMatched))
                                {
                                    sb.Remove(sb.Length - line.Length - 2, line.Length);
                                    mDiffResults.Add(sb.ToString());
                                    sb.Clear();
                                    sb.AppendLine(line);
                                }
                                else if( numPostProcessing == 0 && PostProcessingMatched)
                                {
                                    sb.Clear();
                                    sb.AppendLine(line);
                                    numPostProcessing = 1;
                                }
                                else if (numFailedToComplete == 0 && FailedtoCompleteMatched)
                                {
                                    sb.Clear();
                                    sb.AppendLine(line);
                                    numFailedToComplete = 1;
                                }

                            }
                        }
                    }
                }

                //Add the last one TestCase records.
                mDiffResults.Add(sb.ToString());

                IsProcessOver = true;
                App.Logger.Information($"End of handling Diff File: {diffFilePath}");
            }
            catch( System.Exception )
            {
                App.Logger.Error($"{nameof(DiffDataManager)} LoadAndProcessDiffFile failed!");
                throw;
            }
        } , location);

        return (mDiffResults, mDiffAllNames);
    }

    /// <summary>
    /// Split the diff Result to Baseline and Actual
    /// OldText should start with + m_PlusChar
    /// NewText should start with - m_MinusChar
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private (string baseLineText, string actualText) SplitResultToDiffString(string input)
    {
        // Split content
        var lines = input.Split('\n').ToList();

        // Find the Index of separator 
        var plusIndices = new List<int>();
        var minusIndices = new List<int>();
        for( int i = 0; i < lines.Count; i++ )
        {
            if( lines[i].StartsWith(m_PlusChar) )
            {
                plusIndices.Add(i);
            }
            else if( lines[i].StartsWith(m_MinusChar) )
            {
                minusIndices.Add(i);
            }
        }


        // Create two string lists based on the index
        // OldText_baseLine should start with + m_PlusChar
        // NewText_actual   should start with - m_MinusChar
        var baseLineLines = new List<string>(lines);
        var actualLines = new List<string>(lines);

        // Delete lines beginning with + sign in actualLines to leave the content start with - sign
        for( int i = 0; i < plusIndices.Count; i++ )
        {
            if( actualLines[plusIndices[i] - i].StartsWith(m_PlusChar) )
            {
                actualLines.RemoveAt(plusIndices[i] - i);
            }
        }

        // Delete lines beginning with - sign in baseLineLines to leave the content start with + sign
        for( int i = 0; i < minusIndices.Count; i++ )
        {
            if( baseLineLines[minusIndices[i] - i].StartsWith(m_MinusChar) )
            {
                baseLineLines.RemoveAt(minusIndices[i] - i);
            }
        }

        // Trim the char(s) + of each line of BaseLine
        // Trim the char(s) - of each line of Actual
        if( baseLineLines.Count.Equals(actualLines.Count) )
        {
            for( int i = 0; i < baseLineLines.Count; i++ )
            {
                baseLineLines[i] = baseLineLines[i].TrimStart(m_PlusChar);
                actualLines[i] = actualLines[i].TrimStart(m_MinusChar);
            }
        }
        else
        {
            for( int i = 0; i < baseLineLines.Count; i++ )
            {
                baseLineLines[i] = baseLineLines[i].TrimStart(m_PlusChar);
            }

            for( int i = 0; i < actualLines.Count; i++ )
            {
                actualLines[i] = actualLines[i].TrimStart(m_MinusChar);
            }
        }


        // Concat list and return
        return (string.Join("\n" , baseLineLines), string.Join("\n" , actualLines));
    }




    /// <summary>
    /// Check if the Diff Data handled over.
    /// </summary>
    /// <param name="throwException"> true for throwing an error if IsProcessOver is false </param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public bool CheckProcessOver(bool throwException = true)
    {
        if( !IsProcessOver )
        {
            App.Logger.Error($"{nameof(DiffDataManager)}Haven't load and process the diff data first!!!");
            if( throwException )
            {
                throw new NotSupportedException("Haven't load and process the diff data first!!!");
            }
            return false;
        }
        return true;
    }

}

internal sealed class DiffPlexDiffEngine : IDiffer
{
    public DiffResult CreateCharacterDiffs(string oldText , string newText , bool ignoreWhitespace)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateCharacterDiffs(string oldText , string newText , bool ignoreWhitespace , bool ignoreCase)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateCustomDiffs(string oldText , string newText , bool ignoreWhiteSpace , Func<string , string[]> chunker)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateCustomDiffs(string oldText , string newText , bool ignoreWhiteSpace , bool ignoreCase , Func<string , string[]> chunker)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateDiffs(string oldText , string newText , bool ignoreWhiteSpace , bool ignoreCase , IChunker chunker)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateLineDiffs(string oldText , string newText , bool ignoreWhitespace)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateLineDiffs(string oldText , string newText , bool ignoreWhitespace , bool ignoreCase)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateWordDiffs(string oldText , string newText , bool ignoreWhitespace , char[] separators)
    {
        throw new NotImplementedException();
    }

    public DiffResult CreateWordDiffs(string oldText , string newText , bool ignoreWhitespace , bool ignoreCase , char[] separators)
    {
        throw new NotImplementedException();
    }

}