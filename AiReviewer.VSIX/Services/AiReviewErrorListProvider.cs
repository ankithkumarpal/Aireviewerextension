using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using EnvDTE;
using AiReviewer.Shared.Services;
using AiReviewer.Shared.Models;

namespace AiReviewer.VSIX.Services
{
    internal class AiReviewErrorListProvider : IDisposable
    {
        private readonly ITableManager _tableManager;
        private readonly TableDataSource _dataSource;

        public AiReviewErrorListProvider(IServiceProvider serviceProvider)
        {
            _tableManager = serviceProvider.GetService(typeof(SVsErrorList)) as ITableManager;
            _dataSource = new TableDataSource();
            _tableManager?.AddSource(_dataSource, StandardTableColumnDefinitions.DetailsExpander,
                                                  StandardTableColumnDefinitions.ErrorSeverity,
                                                  StandardTableColumnDefinitions.ErrorCode,
                                                  StandardTableColumnDefinitions.ErrorSource,
                                                  StandardTableColumnDefinitions.BuildTool,
                                                  StandardTableColumnDefinitions.ErrorCategory,
                                                  StandardTableColumnDefinitions.Text,
                                                  StandardTableColumnDefinitions.DocumentName,
                                                  StandardTableColumnDefinitions.Line,
                                                  StandardTableColumnDefinitions.Column);
        }

        public void AddErrors(List<ReviewResult> results)
        {
            _dataSource.Clear();
            foreach (var result in results)
            {
                _dataSource.AddError(result);
            }
        }

        public void Clear()
        {
            _dataSource.Clear();
        }

        public void Dispose()
        {
            _tableManager?.RemoveSource(_dataSource);
        }

        private class TableDataSource : ITableDataSource
        {
            private readonly List<TableEntry> _entries = new List<TableEntry>();
            private readonly List<ITableDataSink> _sinks = new List<ITableDataSink>();

            public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;
            public string Identifier => "AiReviewer";
            public string DisplayName => "AI Reviewer";

            public IDisposable Subscribe(ITableDataSink sink)
            {
                _sinks.Add(sink);
                sink.AddFactory(new TableEntriesSnapshotFactory(this, _entries), removeAllFactories: true);
                return new Subscription(this, sink);
            }

            public void AddError(ReviewResult result)
            {
                _entries.Add(new TableEntry(result));
                NotifyChange();
            }

            public void Clear()
            {
                _entries.Clear();
                NotifyChange();
            }

            private void NotifyChange()
            {
                foreach (var sink in _sinks)
                {
                    sink.FactorySnapshotChanged(null);
                }
            }

            private class Subscription : IDisposable
            {
                private readonly TableDataSource _source;
                private readonly ITableDataSink _sink;

                public Subscription(TableDataSource source, ITableDataSink sink)
                {
                    _source = source;
                    _sink = sink;
                }

                public void Dispose()
                {
                    _source._sinks.Remove(_sink);
                }
            }
        }

        private class TableEntriesSnapshotFactory : ITableEntriesSnapshotFactory
        {
            private readonly TableDataSource _source;
            private readonly List<TableEntry> _entries;
            private int _versionNumber;

            public TableEntriesSnapshotFactory(TableDataSource source, List<TableEntry> entries)
            {
                _source = source;
                _entries = entries;
            }

            public int CurrentVersionNumber => _versionNumber;

            public ITableEntriesSnapshot GetCurrentSnapshot()
            {
                return new TableEntriesSnapshot(_entries, _versionNumber);
            }

            public ITableEntriesSnapshot GetSnapshot(int versionNumber)
            {
                return new TableEntriesSnapshot(_entries, versionNumber);
            } 

            public void Dispose()
            {
            }
        }

        private class TableEntriesSnapshot : ITableEntriesSnapshot
        {
            private readonly List<TableEntry> _entries;
            private readonly int _versionNumber;

            public TableEntriesSnapshot(List<TableEntry> entries, int versionNumber)
            {
                _entries = entries;
                _versionNumber = versionNumber;
            }

            public int Count => _entries.Count;
            public int VersionNumber => _versionNumber;

            public int IndexOf(int currentIndex, ITableEntriesSnapshot newSnapshot)
            {
                return currentIndex;
            }

            public void StartCaching()
            {
            }

            public void StopCaching()
            {
            }

            public bool TryGetValue(int index, string keyName, out object content)
            {
                if (index < 0 || index >= _entries.Count)
                {
                    content = null;
                    return false;
                }

                var entry = _entries[index];
                return entry.TryGetValue(keyName, out content);
            }

            public void Dispose()
            {
            }
        }

        private class TableEntry
        {
            private readonly ReviewResult _result;

            public TableEntry(ReviewResult result)
            {
                _result = result;
            }

            public bool TryGetValue(string keyName, out object content)
            {
                switch (keyName)
                {
                    case StandardTableKeyNames.DocumentName:
                        content = _result.FilePath;
                        return true;
                    case StandardTableKeyNames.Line:
                        content = Math.Max(0, _result.LineNumber - 1); // VS uses 0-based
                        return true;
                    case StandardTableKeyNames.Column:
                        content = 0;
                        return true;
                    case StandardTableKeyNames.Text:
                        content = $"{_result.Issue}\nSuggestion: {_result.Suggestion}";
                        return true;
                    case StandardTableKeyNames.ErrorSeverity:
                        content = _result.Severity == "High" ? __VSERRORCATEGORY.EC_ERROR :
                                 _result.Severity == "Medium" ? __VSERRORCATEGORY.EC_WARNING :
                                 __VSERRORCATEGORY.EC_MESSAGE;
                        return true;
                    case StandardTableKeyNames.ErrorSource:
                        content = ErrorSource.Other;
                        return true;
                    case StandardTableKeyNames.ErrorCode:
                        content = "AI001";
                        return true;
                    case StandardTableKeyNames.BuildTool:
                        content = "AI Reviewer";
                        return true;
                    case StandardTableKeyNames.ErrorCategory:
                        content = string.IsNullOrEmpty(_result.Rule) ? "Code Quality" : _result.Rule;
                        return true;
                    case StandardTableKeyNames.Priority:
                        content = _result.Severity == "High" ? vsTaskPriority.vsTaskPriorityHigh :
                                 _result.Severity == "Medium" ? vsTaskPriority.vsTaskPriorityMedium :
                                 vsTaskPriority.vsTaskPriorityLow;
                        return true;
                    default:
                        content = null;
                        return false;
                }
            }
        }
    }
}
