﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Krüger" email="mike@icsharpcode.net"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Diagnostics;
using System.Text;
using ICSharpCode.TextEditor.Document;

namespace ICSharpCode.TextEditor.Actions
{
    public class Tab : AbstractEditAction
    {
        public static string GetIndentationString(IDocument document)
        {
            return GetIndentationString(document, textArea: null);
        }

        public static string GetIndentationString(IDocument document, TextArea textArea)
        {
            var indent = new StringBuilder();

            if (document.TextEditorProperties.ConvertTabsToSpaces)
            {
                var tabIndent = document.TextEditorProperties.IndentationSize;
                if (textArea != null)
                {
                    var column = textArea.TextView.GetVisualColumn(textArea.Caret.Line, textArea.Caret.Column);
                    indent.Append(new string(c: ' ', tabIndent - column%tabIndent));
                }
                else
                {
                    indent.Append(new string(c: ' ', tabIndent));
                }
            }
            else
            {
                indent.Append(value: '\t');
            }

            return indent.ToString();
        }

        private static void InsertTabs(IDocument document, ISelection selection, int y1, int y2)
        {
            var indentationString = GetIndentationString(document);
            for (var i = y2; i >= y1; --i)
            {
                var line = document.GetLineSegment(i);
                if (i == y2 && i == selection.EndPosition.Y && selection.EndPosition.X == 0)
                    continue;

                // this bit is optional - but useful if you are using block tabbing to sort out
                // a source file with a mixture of tabs and spaces
//                string newLine = document.GetText(line.Offset,line.Length);
//                document.Replace(line.Offset,line.Length,newLine);

                document.Insert(line.Offset, indentationString);
            }
        }

        private static void InsertTabAtCaretPosition(TextArea textArea)
        {
            switch (textArea.Caret.CaretMode)
            {
                case CaretMode.InsertMode:
                    textArea.InsertString(GetIndentationString(textArea.Document, textArea));
                    break;
                case CaretMode.OverwriteMode:
                    var indentStr = GetIndentationString(textArea.Document, textArea);
                    textArea.ReplaceChar(indentStr[index: 0]);
                    if (indentStr.Length > 1)
                        textArea.InsertString(indentStr.Substring(startIndex: 1));
                    break;
            }

            textArea.SetDesiredColumn();
        }

        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.SelectionManager.SelectionIsReadonly)
                return;
            textArea.Document.UndoStack.StartUndoGroup();
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                foreach (var selection in textArea.SelectionManager.SelectionCollection)
                {
                    var startLine = selection.StartPosition.Y;
                    var endLine = selection.EndPosition.Y;
                    if (startLine != endLine)
                    {
                        textArea.BeginUpdate();
                        InsertTabs(textArea.Document, selection, startLine, endLine);
                        textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.LinesBetween, startLine, endLine));
                        textArea.EndUpdate();
                    }
                    else
                    {
                        InsertTabAtCaretPosition(textArea);
                        break;
                    }
                }

                textArea.Document.CommitUpdate();
                textArea.AutoClearSelection = false;
            }
            else
            {
                InsertTabAtCaretPosition(textArea);
            }

            textArea.Document.UndoStack.EndUndoGroup();
        }
    }

    public class ShiftTab : AbstractEditAction
    {
        private static void RemoveTabs(IDocument document, ISelection selection, int y1, int y2)
        {
            document.UndoStack.StartUndoGroup();
            for (var i = y2; i >= y1; --i)
            {
                var line = document.GetLineSegment(i);
                if (i == y2 && line.Offset == selection.EndOffset)
                    continue;
                if (line.Length > 0)
                {
                    var charactersToRemove = 0;
                    if (document.GetCharAt(line.Offset) == '\t')
                    {
                        // first character is a tab - just remove it
                        charactersToRemove = 1;
                    }
                    else if (document.GetCharAt(line.Offset) == ' ')
                    {
                        int leadingSpaces;
                        var tabIndent = document.TextEditorProperties.IndentationSize;
                        for (leadingSpaces = 1;
                            leadingSpaces < line.Length && document.GetCharAt(line.Offset + leadingSpaces) == ' ';
                            leadingSpaces++)
                        {
                            // deliberately empty
                        }

                        if (leadingSpaces >= tabIndent)
                            charactersToRemove = tabIndent;
                        else if (line.Length > leadingSpaces && document.GetCharAt(line.Offset + leadingSpaces) == '\t')
                            charactersToRemove = leadingSpaces + 1;
                        else
                            charactersToRemove = leadingSpaces;
                    }

                    if (charactersToRemove > 0)
                        document.Remove(line.Offset, charactersToRemove);
                }
            }

            document.UndoStack.EndUndoGroup();
        }

        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                foreach (var selection in textArea.SelectionManager.SelectionCollection)
                {
                    var startLine = selection.StartPosition.Y;
                    var endLine = selection.EndPosition.Y;
                    textArea.BeginUpdate();
                    RemoveTabs(textArea.Document, selection, startLine, endLine);
                    textArea.Document.UpdateQueue.Clear();
                    textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.LinesBetween, startLine, endLine));
                    textArea.EndUpdate();
                }

                textArea.AutoClearSelection = false;
            }
            else
            {
                // Pressing Shift-Tab with nothing selected the cursor will move back to the
                // previous tab stop. It will stop at the beginning of the line. Also, the desired
                // column is updated to that column.
                var line = textArea.Document.GetLineSegmentForOffset(textArea.Caret.Offset);
                //var startOfLine = textArea.Document.GetText(line.Offset, textArea.Caret.Offset - line.Offset);
                var tabIndent = textArea.Document.TextEditorProperties.IndentationSize;
                var currentColumn = textArea.Caret.Column;
                var remainder = currentColumn%tabIndent;
                if (remainder == 0)
                    textArea.Caret.DesiredColumn = Math.Max(val1: 0, currentColumn - tabIndent);
                else
                    textArea.Caret.DesiredColumn = Math.Max(val1: 0, currentColumn - remainder);
                textArea.SetCaretToDesiredColumn();
            }
        }
    }

    public class ToggleComment : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.Document.ReadOnly)
                return;

            if (textArea.Document.HighlightingStrategy.Properties.ContainsKey("LineComment"))
                new ToggleLineComment().Execute(textArea);
            else if (textArea.Document.HighlightingStrategy.Properties.ContainsKey("BlockCommentBegin"))
                new ToggleBlockComment().Execute(textArea);
        }
    }

    public class ToggleLineComment : AbstractEditAction
    {
        private int firstLine;
        private int lastLine;

        private void RemoveCommentAt(IDocument document, string comment, ISelection selection, int y1, int y2)
        {
            firstLine = y1;
            lastLine = y2;

            for (var i = y2; i >= y1; --i)
            {
                var line = document.GetLineSegment(i);
                if (selection != null && i == y2 && line.Offset == selection.Offset + selection.Length)
                {
                    --lastLine;
                    continue;
                }

                var lineText = document.GetText(line.Offset, line.Length);
                if (lineText.Trim().StartsWith(comment))
                    document.Remove(line.Offset + lineText.IndexOf(comment), comment.Length);
            }
        }

        private void SetCommentAt(IDocument document, string comment, ISelection selection, int y1, int y2)
        {
            firstLine = y1;
            lastLine = y2;

            for (var i = y2; i >= y1; --i)
            {
                var line = document.GetLineSegment(i);
                if (selection != null && i == y2 && line.Offset == selection.Offset + selection.Length)
                {
                    --lastLine;
                    continue;
                }

//                var lineText = document.GetText(line.Offset, line.Length);
                document.Insert(line.Offset, comment);
            }
        }

        private bool ShouldComment(IDocument document, string comment, ISelection selection, int startLine, int endLine)
        {
            for (var i = endLine; i >= startLine; --i)
            {
                var line = document.GetLineSegment(i);
                if (selection != null && i == endLine && line.Offset == selection.Offset + selection.Length)
                {
                    --lastLine;
                    continue;
                }

                var lineText = document.GetText(line.Offset, line.Length);
                if (!lineText.Trim().StartsWith(comment))
                    return true;
            }

            return false;
        }

        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.Document.ReadOnly)
                return;

            string comment = null;
            if (textArea.Document.HighlightingStrategy.Properties.ContainsKey("LineComment"))
                comment = textArea.Document.HighlightingStrategy.Properties["LineComment"];

            if (comment == null || comment.Length == 0)
                return;

            textArea.Document.UndoStack.StartUndoGroup();
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                var shouldComment = true;
                foreach (var selection in textArea.SelectionManager.SelectionCollection)
                    if (!ShouldComment(textArea.Document, comment, selection, selection.StartPosition.Y, selection.EndPosition.Y))
                    {
                        shouldComment = false;
                        break;
                    }

                foreach (var selection in textArea.SelectionManager.SelectionCollection)
                {
                    textArea.BeginUpdate();
                    if (shouldComment)
                        SetCommentAt(textArea.Document, comment, selection, selection.StartPosition.Y, selection.EndPosition.Y);
                    else
                        RemoveCommentAt(textArea.Document, comment, selection, selection.StartPosition.Y, selection.EndPosition.Y);
                    textArea.Document.UpdateQueue.Clear();
                    textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.LinesBetween, firstLine, lastLine));
                    textArea.EndUpdate();
                }

                textArea.Document.CommitUpdate();
                textArea.AutoClearSelection = false;
            }
            else
            {
                textArea.BeginUpdate();
                var caretLine = textArea.Caret.Line;
                if (ShouldComment(textArea.Document, comment, selection: null, caretLine, caretLine))
                    SetCommentAt(textArea.Document, comment, selection: null, caretLine, caretLine);
                else
                    RemoveCommentAt(textArea.Document, comment, selection: null, caretLine, caretLine);
                textArea.Document.UpdateQueue.Clear();
                textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.SingleLine, caretLine));
                textArea.EndUpdate();
            }

            textArea.Document.UndoStack.EndUndoGroup();
        }
    }

    public class ToggleBlockComment : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.Document.ReadOnly)
                return;

            string commentStart = null;
            if (textArea.Document.HighlightingStrategy.Properties.ContainsKey("BlockCommentBegin"))
                commentStart = textArea.Document.HighlightingStrategy.Properties["BlockCommentBegin"];

            string commentEnd = null;
            if (textArea.Document.HighlightingStrategy.Properties.ContainsKey("BlockCommentEnd"))
                commentEnd = textArea.Document.HighlightingStrategy.Properties["BlockCommentEnd"];

            if (commentStart == null || commentStart.Length == 0 || commentEnd == null || commentEnd.Length == 0)
                return;

            int selectionStartOffset;
            int selectionEndOffset;

            if (textArea.SelectionManager.HasSomethingSelected)
            {
                selectionStartOffset = textArea.SelectionManager.SelectionCollection[index: 0].Offset;
                selectionEndOffset = textArea.SelectionManager.SelectionCollection[textArea.SelectionManager.SelectionCollection.Count - 1].EndOffset;
            }
            else
            {
                selectionStartOffset = textArea.Caret.Offset;
                selectionEndOffset = selectionStartOffset;
            }

            var commentRegion = FindSelectedCommentRegion(textArea.Document, commentStart, commentEnd, selectionStartOffset, selectionEndOffset);

            textArea.Document.UndoStack.StartUndoGroup();
            if (commentRegion != null)
                RemoveComment(textArea.Document, commentRegion);
            else if (textArea.SelectionManager.HasSomethingSelected)
                SetCommentAt(textArea.Document, selectionStartOffset, selectionEndOffset, commentStart, commentEnd);
            textArea.Document.UndoStack.EndUndoGroup();

            textArea.Document.CommitUpdate();
            textArea.AutoClearSelection = false;
        }

        public static BlockCommentRegion FindSelectedCommentRegion(IDocument document, string commentStart, string commentEnd, int selectionStartOffset, int selectionEndOffset)
        {
            if (document.TextLength == 0)
                return null;

            // Find start of comment in selected text.

            int commentEndOffset;
            var selectedText = document.GetText(selectionStartOffset, selectionEndOffset - selectionStartOffset);

            var commentStartOffset = selectedText.IndexOf(commentStart);
            if (commentStartOffset >= 0)
                commentStartOffset += selectionStartOffset;

            // Find end of comment in selected text.

            if (commentStartOffset >= 0)
                commentEndOffset = selectedText.IndexOf(commentEnd, commentStartOffset + commentStart.Length - selectionStartOffset);
            else
                commentEndOffset = selectedText.IndexOf(commentEnd);

            if (commentEndOffset >= 0)
                commentEndOffset += selectionStartOffset;

            // Find start of comment before or partially inside the
            // selected text.

            if (commentStartOffset == -1)
            {
                var offset = selectionEndOffset + commentStart.Length - 1;
                if (offset > document.TextLength)
                    offset = document.TextLength;
                var text = document.GetText(offset: 0, offset);
                commentStartOffset = text.LastIndexOf(commentStart);
                if (commentStartOffset >= 0)
                {
                    // Find end of comment before comment start.
                    var commentEndBeforeStartOffset = text.IndexOf(commentEnd, commentStartOffset, selectionStartOffset - commentStartOffset);
                    if (commentEndBeforeStartOffset > commentStartOffset)
                        commentStartOffset = -1;
                }
            }

            // Find end of comment after or partially after the
            // selected text.

            if (commentEndOffset == -1)
            {
                var offset = selectionStartOffset + 1 - commentEnd.Length;
                if (offset < 0)
                    offset = selectionStartOffset;
                var text = document.GetText(offset, document.TextLength - offset);
                commentEndOffset = text.IndexOf(commentEnd);
                if (commentEndOffset >= 0)
                    commentEndOffset += offset;
            }

            if (commentStartOffset != -1 && commentEndOffset != -1)
                return new BlockCommentRegion(commentStart, commentEnd, commentStartOffset, commentEndOffset);

            return null;
        }

        private static void SetCommentAt(IDocument document, int offsetStart, int offsetEnd, string commentStart, string commentEnd)
        {
            document.Insert(offsetEnd, commentEnd);
            document.Insert(offsetStart, commentStart);
        }

        private static void RemoveComment(IDocument document, BlockCommentRegion commentRegion)
        {
            document.Remove(commentRegion.EndOffset, commentRegion.CommentEnd.Length);
            document.Remove(commentRegion.StartOffset, commentRegion.CommentStart.Length);
        }
    }

    public class BlockCommentRegion
    {
        /// <summary>
        ///     The end offset is the offset where the comment end string starts from.
        /// </summary>
        public BlockCommentRegion(string commentStart, string commentEnd, int startOffset, int endOffset)
        {
            CommentStart = commentStart;
            CommentEnd = commentEnd;
            StartOffset = startOffset;
            EndOffset = endOffset;
        }

        public string CommentStart { get; } = string.Empty;

        public string CommentEnd { get; } = string.Empty;

        public int StartOffset { get; } = -1;

        public int EndOffset { get; } = -1;

        public override int GetHashCode()
        {
            var hashCode = 0;
            unchecked
            {
                if (CommentStart != null) hashCode += 1000000007*CommentStart.GetHashCode();
                if (CommentEnd != null) hashCode += 1000000009*CommentEnd.GetHashCode();
                hashCode += 1000000021*StartOffset.GetHashCode();
                hashCode += 1000000033*EndOffset.GetHashCode();
            }

            return hashCode;
        }

        public override bool Equals(object obj)
        {
            var other = obj as BlockCommentRegion;
            if (other == null) return false;
            return CommentStart == other.CommentStart && CommentEnd == other.CommentEnd && StartOffset == other.StartOffset && EndOffset == other.EndOffset;
        }
    }

    public class Backspace : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                Delete.DeleteSelection(textArea);
            }
            else
            {
                if (textArea.Caret.Offset > 0 && !textArea.IsReadOnly(textArea.Caret.Offset - 1))
                {
                    textArea.BeginUpdate();
                    var curLineNr = textArea.Document.GetLineNumberForOffset(textArea.Caret.Offset);
                    var curLineOffset = textArea.Document.GetLineSegment(curLineNr).Offset;

                    if (curLineOffset == textArea.Caret.Offset)
                    {
                        var line = textArea.Document.GetLineSegment(curLineNr - 1);
//                        var lastLine = curLineNr == textArea.Document.TotalNumberOfLines;
                        var lineEndOffset = line.Offset + line.Length;
                        var lineLength = line.Length;
                        textArea.Document.Remove(lineEndOffset, curLineOffset - lineEndOffset);
                        textArea.Caret.Position = new TextLocation(lineLength, curLineNr - 1);
                        textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, curLineNr - 1)));
                    }
                    else
                    {
                        var caretOffset = textArea.Caret.Offset - 1;
                        textArea.Caret.Position = textArea.Document.OffsetToPosition(caretOffset);
                        textArea.Document.Remove(caretOffset, length: 1);

                        textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToLineEnd, new TextLocation(textArea.Caret.Offset - textArea.Document.GetLineSegment(curLineNr).Offset, curLineNr)));
                    }

                    textArea.EndUpdate();
                }
            }
        }
    }

    public class Delete : AbstractEditAction
    {
        internal static void DeleteSelection(TextArea textArea)
        {
            Debug.Assert(textArea.SelectionManager.HasSomethingSelected);
            if (textArea.SelectionManager.SelectionIsReadonly)
                return;
            textArea.BeginUpdate();
            textArea.Caret.Position = textArea.SelectionManager.SelectionCollection[index: 0].StartPosition;
            textArea.SelectionManager.RemoveSelectedText();
            textArea.ScrollToCaret();
            textArea.EndUpdate();
        }

        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                DeleteSelection(textArea);
            }
            else
            {
                if (textArea.IsReadOnly(textArea.Caret.Offset))
                    return;

                if (textArea.Caret.Offset < textArea.Document.TextLength)
                {
                    textArea.BeginUpdate();
                    var curLineNr = textArea.Document.GetLineNumberForOffset(textArea.Caret.Offset);
                    var curLine = textArea.Document.GetLineSegment(curLineNr);

                    if (curLine.Offset + curLine.Length == textArea.Caret.Offset)
                    {
                        if (curLineNr + 1 < textArea.Document.TotalNumberOfLines)
                        {
                            var nextLine = textArea.Document.GetLineSegment(curLineNr + 1);

                            textArea.Document.Remove(textArea.Caret.Offset, nextLine.Offset - textArea.Caret.Offset);
                            textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, curLineNr)));
                        }
                    }
                    else
                    {
                        textArea.Document.Remove(textArea.Caret.Offset, length: 1);
//                        textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToLineEnd, new TextLocation(textArea.Caret.Offset - textArea.Document.GetLineSegment(curLineNr).Offset, curLineNr)));
                    }

                    textArea.UpdateMatchingBracket();
                    textArea.EndUpdate();
                }
            }
        }
    }

    public class MovePageDown : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            var curLineNr = textArea.Caret.Line;
            var requestedLineNumber = Math.Min(textArea.Document.GetNextVisibleLineAbove(curLineNr, textArea.TextView.VisibleLineCount - 2), textArea.Document.TotalNumberOfLines - 1);

            if (curLineNr != requestedLineNumber)
            {
                textArea.Caret.Position = new TextLocation(column: 0, requestedLineNumber);
                textArea.SetCaretToDesiredColumn();
            }
        }
    }

    public class MovePageUp : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            var curLineNr = textArea.Caret.Line;
            var requestedLineNumber = Math.Max(textArea.Document.GetNextVisibleLineBelow(curLineNr, textArea.TextView.VisibleLineCount - 2), val2: 0);

            if (curLineNr != requestedLineNumber)
            {
                textArea.Caret.Position = new TextLocation(column: 0, requestedLineNumber);
                textArea.SetCaretToDesiredColumn();
            }
        }
    }

    public class Return : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="TextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.Document.ReadOnly)
                return;
            textArea.BeginUpdate();
            textArea.Document.UndoStack.StartUndoGroup();
            try
            {
                if (textArea.HandleKeyPress(ch: '\n'))
                    return;

                textArea.InsertString(Environment.NewLine);

                var curLineNr = textArea.Caret.Line;
                textArea.Document.FormattingStrategy.FormatLine(textArea, curLineNr, textArea.Caret.Offset, charTyped: '\n');
                textArea.SetDesiredColumn();

                textArea.Document.UpdateQueue.Clear();
                textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, curLineNr - 1)));
            }
            finally
            {
                textArea.Document.UndoStack.EndUndoGroup();
                textArea.EndUpdate();
            }
        }
    }

    public class ToggleEditMode : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.Document.ReadOnly)
                return;
            switch (textArea.Caret.CaretMode)
            {
                case CaretMode.InsertMode:
                    textArea.Caret.CaretMode = CaretMode.OverwriteMode;
                    break;
                case CaretMode.OverwriteMode:
                    textArea.Caret.CaretMode = CaretMode.InsertMode;
                    break;
            }
        }
    }

    public class Undo : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            textArea.MotherTextEditorControl.Undo();
        }
    }

    public class Redo : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            textArea.MotherTextEditorControl.Redo();
        }
    }

    /// <summary>
    ///     handles the ctrl-backspace key
    ///     functionality attempts to roughly mimic MS Developer studio
    ///     I will implement this as deleting back to the point that ctrl-leftarrow would
    ///     take you to
    /// </summary>
    public class WordBackspace : AbstractEditAction
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            // if anything is selected we will just delete it first
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                Delete.DeleteSelection(textArea);
                return;
            }

            textArea.BeginUpdate();
            // now delete from the caret to the beginning of the word
            var line =
                textArea.Document.GetLineSegmentForOffset(textArea.Caret.Offset);
            // if we are not at the beginning of a line
            if (textArea.Caret.Offset > line.Offset)
            {
                var prevWordStart = TextUtilities.FindPrevWordStart(
                    textArea.Document,
                    textArea.Caret.Offset);
                if (prevWordStart < textArea.Caret.Offset)
                    if (!textArea.IsReadOnly(prevWordStart, textArea.Caret.Offset - prevWordStart))
                    {
                        textArea.Document.Remove(
                            prevWordStart,
                            textArea.Caret.Offset - prevWordStart);
                        textArea.Caret.Position = textArea.Document.OffsetToPosition(prevWordStart);
                    }
            }

            // if we are now at the beginning of a line
            if (textArea.Caret.Offset == line.Offset)
            {
                // if we are not on the first line
                var curLineNr = textArea.Document.GetLineNumberForOffset(textArea.Caret.Offset);
                if (curLineNr > 0)
                {
                    // move to the end of the line above
                    var lineAbove = textArea.Document.GetLineSegment(curLineNr - 1);
                    var endOfLineAbove = lineAbove.Offset + lineAbove.Length;
                    var charsToDelete = textArea.Caret.Offset - endOfLineAbove;
                    if (!textArea.IsReadOnly(endOfLineAbove, charsToDelete))
                    {
                        textArea.Document.Remove(endOfLineAbove, charsToDelete);
                        textArea.Caret.Position = textArea.Document.OffsetToPosition(endOfLineAbove);
                    }
                }
            }

            textArea.SetDesiredColumn();
            textArea.EndUpdate();
            // if there are now less lines, we need this or there are redraw problems
            textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, textArea.Document.GetLineNumberForOffset(textArea.Caret.Offset))));
            textArea.Document.CommitUpdate();
        }
    }

    /// <summary>
    ///     handles the ctrl-delete key
    ///     functionality attempts to mimic MS Developer studio
    ///     I will implement this as deleting forwardto the point that
    ///     ctrl-leftarrow would take you to
    /// </summary>
    public class DeleteWord : Delete
    {
        /// <remarks>
        ///     Executes this edit action
        /// </remarks>
        /// <param name="textArea">The <see cref="ItextArea" /> which is used for callback purposes</param>
        public override void Execute(TextArea textArea)
        {
            if (textArea.SelectionManager.HasSomethingSelected)
            {
                DeleteSelection(textArea);
                return;
            }

            // if anything is selected we will just delete it first
            textArea.BeginUpdate();
            // now delete from the caret to the beginning of the word
            var line = textArea.Document.GetLineSegmentForOffset(textArea.Caret.Offset);
            if (textArea.Caret.Offset == line.Offset + line.Length)
            {
                // if we are at the end of a line
                base.Execute(textArea);
            }
            else
            {
                var nextWordStart = TextUtilities.FindNextWordStart(
                    textArea.Document,
                    textArea.Caret.Offset);
                if (nextWordStart > textArea.Caret.Offset)
                    if (!textArea.IsReadOnly(textArea.Caret.Offset, nextWordStart - textArea.Caret.Offset))
                        textArea.Document.Remove(textArea.Caret.Offset, nextWordStart - textArea.Caret.Offset);
            }

            textArea.UpdateMatchingBracket();
            textArea.EndUpdate();
            // if there are now less lines, we need this or there are redraw problems
            textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, textArea.Document.GetLineNumberForOffset(textArea.Caret.Offset))));
            textArea.Document.CommitUpdate();
        }
    }

    public class DeleteLine : AbstractEditAction
    {
        public override void Execute(TextArea textArea)
        {
            var lineNr = textArea.Caret.Line;
            var line = textArea.Document.GetLineSegment(lineNr);
            if (textArea.IsReadOnly(line.Offset, line.Length))
                return;
            textArea.Document.Remove(line.Offset, line.TotalLength);
            textArea.Caret.Position = textArea.Document.OffsetToPosition(line.Offset);

            textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, lineNr)));
            textArea.UpdateMatchingBracket();
            textArea.Document.CommitUpdate();
        }
    }

    public class MoveLineUp : AbstractEditAction
    {
        public override void Execute(TextArea textArea)
        {
            int caretInitialLine = textArea.Caret.Line;
            int caretInitialColumn = textArea.Caret.Column;
            if (MoveLine.TrySwitchLines(textArea, caretInitialLine - 1))
            {
                textArea.Caret.Position = new TextLocation(caretInitialColumn, caretInitialLine - 1);
            }
        }
    }

    public class MoveLineDown : AbstractEditAction
    {
        public override void Execute(TextArea textArea)
        {
            int caretInitialLine = textArea.Caret.Line;
            int caretInitialColumn = textArea.Caret.Column;
            if (MoveLine.TrySwitchLines(textArea, caretInitialLine))
            {
                textArea.Caret.Position = new TextLocation(caretInitialColumn, caretInitialLine + 1);
            }
        }
    }

    public static class MoveLine
    {
        public static bool TrySwitchLines(TextArea textArea, int firstLineIndex)
        {
            if (textArea.Document.ReadOnly
                || firstLineIndex >= textArea.Document.TotalNumberOfLines - 1
                || firstLineIndex < 0)
            {
                return false;
            }

            try
            {
                LineSegment firstLine = textArea.Document.GetLineSegment(firstLineIndex);
                LineSegment secondLine = textArea.Document.GetLineSegment(firstLineIndex + 1);

                string firstLineContent = textArea.Document.GetText(firstLine.Offset, firstLine.TotalLength);
                string secondLineContent = textArea.Document.GetText(secondLine.Offset, secondLine.TotalLength);

                // Handling of special case where last line that could have no eol char (that is taken from 1st line)
                string newContent = secondLine.DelimiterLength != 0
                    ? secondLineContent + firstLineContent
                    : secondLineContent
                        + firstLineContent.Substring(firstLineContent.Length - firstLine.DelimiterLength)
                        + firstLineContent.Substring(0, firstLineContent.Length - firstLine.DelimiterLength);
                textArea.Document.Replace(firstLine.Offset, firstLine.TotalLength + secondLine.TotalLength, newContent);

                textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.PositionToEnd, new TextLocation(column: 0, firstLineIndex)));
                textArea.UpdateMatchingBracket();
                textArea.Document.CommitUpdate();
                return true;
            }
            catch (Exception)
            {
                // We don't want to crash for a non-essential feature...
                return false;
            }
        }
    }

    public class DeleteToLineEnd : AbstractEditAction
    {
        public override void Execute(TextArea textArea)
        {
            var lineNr = textArea.Caret.Line;
            var line = textArea.Document.GetLineSegment(lineNr);

            var numRemove = line.Offset + line.Length - textArea.Caret.Offset;
            if (numRemove > 0 && !textArea.IsReadOnly(textArea.Caret.Offset, numRemove))
            {
                textArea.Document.Remove(textArea.Caret.Offset, numRemove);
                textArea.Document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.SingleLine, new TextLocation(column: 0, lineNr)));
                textArea.Document.CommitUpdate();
            }
        }
    }

    public class GotoMatchingBrace : AbstractEditAction
    {
        public override void Execute(TextArea textArea)
        {
            var highlight = textArea.FindMatchingBracketHighlight();
            if (highlight != null)
            {
                var p1 = new TextLocation(highlight.CloseBrace.X + 1, highlight.CloseBrace.Y);
                var p2 = new TextLocation(highlight.OpenBrace.X + 1, highlight.OpenBrace.Y);
                if (p1 == textArea.Caret.Position)
                {
                    if (textArea.Document.TextEditorProperties.BracketMatchingStyle == BracketMatchingStyle.After)
                        textArea.Caret.Position = p2;
                    else
                        textArea.Caret.Position = new TextLocation(p2.X - 1, p2.Y);
                }
                else
                {
                    if (textArea.Document.TextEditorProperties.BracketMatchingStyle == BracketMatchingStyle.After)
                        textArea.Caret.Position = p1;
                    else
                        textArea.Caret.Position = new TextLocation(p1.X - 1, p1.Y);
                }

                textArea.SetDesiredColumn();
            }
        }
    }
}
