using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using InGameCodeEditor.Lexer;
using System.Reflection;

namespace InGameCodeEditor
{
    /// <summary>
    /// The main InGame Code Editor component for displaying a syntax highlighting code editor UI element.
    /// </summary>
    public class CodeEditor : MonoBehaviour
    {
        // Private 
        private static readonly KeyCode[] focusKeys = { KeyCode.Return, KeyCode.Backspace, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow };
        private static StringBuilder highlightedBuilder = new StringBuilder(4096);
        private static StringBuilder lineBuilder = new StringBuilder();
        private static MethodInfo scrollBarUpdateFix = null;

        private InputStringLexer lexer = new InputStringLexer();
        private RectTransform inputTextTransform = null;
        private RectTransform lineHighlightTransform = null;
        private int lineCount = 0;
        private int currentLine = 0;
        private int currentColumn = 0;
        private int currentIndent = 0;
        private string lastText = null;
        private bool delayedRefresh = false;
        private float lastScrollValue = 0f;
        private bool lineHighlightLocked = false;

        // Complains about references never assigned but they are inspector values
#pragma warning disable 0649
        [Header("Elements")]
        [SerializeField]
        private TMP_InputField inputField;
        [SerializeField]
        private TextMeshProUGUI inputText;
        [SerializeField]
        private TextMeshProUGUI inputHighlightText;
        [SerializeField]
        private TextMeshProUGUI lineText;
        [SerializeField]
        private Image background;
        [SerializeField]
        private Image lineHighlight;
        [SerializeField]
        private Image lineNumberBackground;
        [SerializeField]
        private Image scrollbar;
        
        [Header("Themes")]
        [SerializeField]
        private CodeEditorTheme editorTheme = null;
        [SerializeField]
        private CodeLanguageTheme languageTheme = null;

        [Header("Options")]
        [SerializeField]
        private bool lineNumbers = true;
        [SerializeField]
        private int lineNumbersSize = 20;

#if UNITY_2018_2_OR_NEWER
        [Header("TMP Compatibility")]
        [SerializeField]
        private bool applyLineOffsetFix = false;
#endif
#pragma warning restore 0649


        // Properties
        /// <summary>
        /// The current editor theme that is being used by the code editor.
        /// This value will be null if no theme is assigned but the code editor will revert to built in default colors.
        /// </summary>
        public CodeEditorTheme EditorTheme
        {
            get { return editorTheme; }
            set
            {
                editorTheme = value;
                ApplyTheme();
            }
        }

        /// <summary>
        /// The current language theme that is being used by the code editor.
        /// The language theme controls which aspects of the text are syntax highlighted.
        /// You can set this value to null to disable syntax highlighting.
        /// </summary>
        public CodeLanguageTheme LanguageTheme
        {
            get { return languageTheme; }
            set
            {                
                languageTheme = value;
                ApplyLanguage();
            }
        }

        /// <summary>
        /// Get the TextMesh Pro input field that this code editor is managing.
        /// </summary>
        public TMP_InputField InputField
        {
            get { return inputField; }
        }

        /// <summary>
        /// Get the total number of lines that the text occupies.
        /// </summary>
        public int LineCount
        {
            get { return lineCount; }
        }

        /// <summary>
        /// Get the current line number for the caret position.
        /// </summary>
        public int CurrentLine
        {
            get { return currentLine; }
        }

        /// <summary>
        /// Get the current column number for the caret position.
        /// </summary>
        public int CurrentColumn
        {
            get { return currentColumn; }
        }

        /// <summary>
        /// Get the current indent level for the caret position.
        /// </summary>
        public int CurrentIndent
        {
            get { return currentIndent; }
        }

        /// <summary>
        /// The text of the code editor input field.
        /// Assigning text will automatically cause a refresh so you do not need to call it manually.
        /// </summary>
        public string Text
        {
            get { return inputField.text; }
            set
            {
                bool empty = string.IsNullOrEmpty(value);

                if (empty == false)
                {
                    inputField.text = value;
                    inputHighlightText.text = value;

                    // Nasty hack to force TMP to update the scroll bar because in some cases it will fail to do so.
                    try
                    {
                        if(scrollBarUpdateFix == null)
                        {
                            scrollBarUpdateFix = typeof(TMP_InputField).GetMethod("UpdateScrollbar", BindingFlags.Instance | BindingFlags.NonPublic);
                        }

                        // Invoke the method
                        scrollBarUpdateFix.Invoke(inputField, null);
                    }
                    catch { }

                    //inputField.ForceLabelUpdate();
                    //inputText.SetText(value, true);
                    delayedRefresh = true;

                    inputText.ForceMeshUpdate(false);
                }
                else
                {
                    inputField.text = string.Empty;
                    inputHighlightText.text = string.Empty;
                    inputText.ForceMeshUpdate(false);
                }
            }
        }

        /// <summary>
        /// Get the current text including xml color tags generated by the syntax highlighter.
        /// </summary>
        public string HighlightedText
        {
            get { return inputHighlightText.text; }
        }

        /// <summary>
        /// Is the line numbers column enabled.
        /// Setting this value to false will cause the column to be hidden.
        /// </summary>
        public bool LineNumbers
        {
            get { return lineNumbers; }
            set
            {
                lineNumbers = value;

                RectTransform inputFieldTransform = inputField.transform as RectTransform;
                RectTransform lineNumberBackgroudTransform = lineNumberBackground.transform as RectTransform;

                // Check for line numbers
                if (lineNumbers == true)
                {
                    // Enable line numbers
                    lineNumberBackground.gameObject.SetActive(true);
                    lineText.gameObject.SetActive(true);

                    // Set left value
                    inputFieldTransform.offsetMin = new Vector2(lineNumbersSize, inputFieldTransform.offsetMin.y);
                    lineNumberBackgroudTransform.sizeDelta = new Vector2(lineNumbersSize + 15, lineNumberBackgroudTransform.sizeDelta.y);
                }
                else
                {
                    // Disable line numbers
                    lineNumberBackground.gameObject.SetActive(false);
                    lineText.gameObject.SetActive(false);

                    // Set left value
                    inputFieldTransform.offsetMin = new Vector2(0, inputFieldTransform.offsetMin.y);
                }
            }
        }

        /// <summary>
        /// The current size of the line number column.
        /// Default size is 20.
        /// </summary>
        public int LineNumbersSize
        {
            get { return lineNumbersSize; }
            set
            {
                lineNumbersSize = value;

                // Update the line numbers
                LineNumbers = lineNumbers;
            }
        }

        // Methods
#if UNITY_EDITOR
        /// <summary>
        /// Called by Unity.
        /// </summary>
        public void OnValidate()
        {
            // Update line numbers
            LineNumbersSize = lineNumbersSize;

            // Appy the theme
            if (AllReferencesAssigned() == true)
                if (editorTheme != null)
                    ApplyTheme();

            // Rebuild language colors
            if (languageTheme != null)
                languageTheme.Invalidate();
        }
#endif

        /// <summary>
        /// Called by Unity.
        /// </summary>
        public void Awake()
        {
            // Check for invalid references
            if(AllReferencesAssigned() == false)
            {
                enabled = false;
                throw new MissingReferenceException("One or more required references are missing. Make sure all references under the 'Elements' header are assigned");
            }

            // Cache transform
            this.inputTextTransform = inputText.GetComponent<RectTransform>();
            this.lineHighlightTransform = lineHighlight.GetComponent<RectTransform>();
        }

        /// <summary>
        /// Called by Unity.
        /// </summary>
        public void Start()
        {
            // Load default theme
            if (editorTheme == null)
                editorTheme = CodeEditorTheme.DefaultTheme;

            // Apply the theme
            ApplyTheme();
            ApplyLanguage();

            // ========================================================
            // 🔥 [핵심 추가] TMP 기본 탭(\t) 입력 차단!
            // 이 스크립트의 LateUpdate에서 스페이스 4칸을 알아서 넣어주므로,
            // TMP가 중복으로 탭을 넣으려고 하는 것만 원천 차단합니다.
            // ========================================================
            if (inputField != null)
            {
                inputField.onValidateInput += (string text, int charIndex, char addedChar) => {
                    return addedChar == '\t' ? '\0' : addedChar;
                };
            }
        }

        /// <summary>
        /// Called by Unity.
        /// </summary>
public void LateUpdate()
{
    // 1. 스크롤 및 하이라이트 업데이트 (기본 기능 유지)
    if (Input.mouseScrollDelta != Vector2.zero || inputField.verticalScrollbar.value != lastScrollValue)
    {
        UpdateCurrentLineHighlight();
        lastScrollValue = inputField.verticalScrollbar.value;
    }

    // 2. 포커스가 있을 때만 Tab / Backspace / Return 처리
    if (inputField.isFocused == true)
    {
        inputHighlightText.text = inputField.text + Input.compositionString;
        inputField.textComponent.ForceMeshUpdate();
        inputHighlightText.ForceMeshUpdate();
        // [Tab 키 처리] 4칸 공백 삽입
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            int pos = inputField.stringPosition;
            inputField.text = inputField.text.Insert(pos, "    ");
            inputField.stringPosition = pos + 4;
            
            // 유니티가 다음 UI로 포커스를 옮기지 못하게 강제 고정
            inputField.ActivateInputField(); 
            Refresh(true);
        }

        // [Backspace 처리] 앞의 4글자가 공백이면 한꺼번에 삭제
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            int pos = inputField.stringPosition;
            if (pos >= 4 && inputField.text.Substring(pos - 4, 4) == "    ")
            {
                inputField.text = inputField.text.Remove(pos - 4, 4);
                inputField.stringPosition = pos - 4;
                Refresh(true);
            }
        }

        // [Return 처리] 이미 수정하신 AutoIndentCaret 호출
        if (Input.GetKeyDown(KeyCode.Return))
        {
            AutoIndentCaret(false);
        }
        else if (Input.anyKeyDown && Input.inputString.Contains(languageTheme.autoIndent.IndentDecreaseString))
        {
            AutoIndentCaret(true);
        }
    }

    // 3. 텍스트 갱신 및 붙여넣기 처리 (기본 기능 유지)
    if (inputField.isFocused || delayedRefresh)
    {
        if (delayedRefresh)
        {
            delayedRefresh = false;
            Refresh(true, false);
        }

        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.V))
        {
            delayedRefresh = true;
        }

        if (Input.anyKey) Refresh();

        // 방향키나 마우스 클릭 시 라인 하이라이트 업데이트
        bool focusKeyPressed = false;
        foreach (KeyCode key in focusKeys)
        {
            if (Input.GetKey(key)) { focusKeyPressed = true; break; }
        }

        if (focusKeyPressed || Input.GetMouseButton(0))
            UpdateCurrentLineHighlight();
    }
}

        /// <summary>
        /// Causes the displayed text content to be refreshed and rehighlighted if it has changed.
        /// </summary>
        /// <param name="forceUpdate">Forcing an update will cause the text to be refreshed even if it has not changed</param>
        /// <param name="updateLineOnly">Should only the current line be refreshed or the whole text</param>
        public void Refresh(bool forceUpdate = false, bool updateLineOnly = true)
        {            
            // Trigger a content change event
            DisplayedContentChanged(inputField.text, forceUpdate, updateLineOnly);
        }

        /// <summary>
        /// Set the line where the line highlight bar will be positioned. Valid line numbers start at 1 and count up until <see cref="LineCount"/>.
        /// You may also like to lock the line highlight bar in position to prevent it being moved by the user which can be achieved by passing 'true' as second argument.
        /// </summary>
        /// <param name="lineNumber">The absolute line number to move the line highlight bar to</param>
        /// <param name="lockLineHighlight">True if the line highlight bar should be locked after moving to the specified line or false if the line bar should be unlocked.</param>
        public void SetLineHighlight(int lineNumber, bool lockLineHighlight)
        {
            // Check if code editor is not active
            if (isActiveAndEnabled == false || lineNumber < 1 || lineNumber > LineCount)
                return;

            int lineOffset = 0;
            int lineIndex = lineNumber - 1;// inputText.textInfo.lineCount - lineNumber - 1;

#if UNITY_2018_2_OR_NEWER
            if(applyLineOffsetFix == true)
                lineOffset++;
#endif

            // Highlight the current line
            lineHighlightTransform.anchoredPosition = new Vector2(5, 
                (inputText.textInfo.lineInfo[inputText.textInfo.characterInfo[0].lineNumber].lineHeight *
                -lineIndex) + lineOffset - 4f +
                inputTextTransform.anchoredPosition.y);

            // Lock the line highlight so it cannot be moved
            if (lockLineHighlight == true)
                LockLineHighlight();
            else
                UnlockLineHighlight();
        }

        /// <summary>
        /// Lock the line highlight bar at the current line. Mouse or keyboard events will not affect the position of the line highlight bar until <see cref="UnlockLineHighlight"/> is called.
        /// </summary>
        public void LockLineHighlight()
        {
            lineHighlightLocked = true;
        }

        /// <summary>
        /// Unlock the line highlight bar. Mouse or keyboard events will cause the line highlight bar to be updated as the user moves to different lines.
        /// </summary>
        public void UnlockLineHighlight()
        {
            lineHighlightLocked = false;
        }

        private void DisplayedContentChanged(string newText, bool forceUpdate, bool updateLineOnly)
        {
            // Update caret position
            UpdateCurrentLineColumnIndent();

            // Check for change
            if ((forceUpdate == false && lastText == newText) || string.IsNullOrEmpty(newText) == true)
            {
                if(string.IsNullOrEmpty(newText) == true)
                {
                    inputHighlightText.text = string.Empty;
                }

                // Its possible the text was cleared so we need to sync numbers and highlighter
                UpdateCurrentLineNumbers();
                UpdateCurrentLineHighlight();
                return;
            }

            //if (updateLineOnly == false)
            //{
                // Run parser to highlight keywords
                inputHighlightText.text = SyntaxHighlightContent(newText);
            //}
            //else
            //{
            //    // Get the caret position
            //    int editIndex = inputField.stringPosition;

            //    // Get the current line
            //    TMP_LineInfo line = inputText.textInfo.lineInfo[currentLine];

            //    int start = line.firstCharacterIndex;
            //    int length = line.characterCount;

            //    // Get the substring
            //    string workingString = newText.Substring(start, length);

            //    // Run the parser on the line
            //    string highlightedWorkingString = SyntaxHighlightContent(workingString);

            //    // Insert the highlighted text
            //    inputHighlightText.text = inputHighlightText.text.Remove(start, length - 1);
            //    inputHighlightText.text = inputHighlightText.text.Insert(start, highlightedWorkingString);
            //}

            // Autohide scrollbar
            bool showScrollbar = inputField.verticalScrollbar.size < 1f;
            
            // Show the scrollbar
            inputField.verticalScrollbar.gameObject.SetActive(showScrollbar);


            // Sync line numbers and update the line highlight
            UpdateCurrentLineNumbers();
            UpdateCurrentLineHighlight();

            this.lastText = newText;
        }

        private void UpdateCurrentLineNumbers()
        {
            // Get the line count
            int currentLineCount = inputText.textInfo.lineCount;

            int currentLineNumber = 1;

            // Check for a change in line
            if (currentLineCount != lineCount)
            {
                // Update line numbers
                lineBuilder.Length = 0;

                // Build line numbers string
                for (int i = 1; i < currentLineCount + 2; i++)
                {
                    if (i - 1 > 0 && i - 1 < currentLineCount - 1)
                    {
                        int characterStart = inputText.textInfo.lineInfo[i - 1].firstCharacterIndex;
                        int characterCount = inputText.textInfo.lineInfo[i - 1].characterCount;

                        if (characterCount != 0 && inputText.text.Substring(characterStart, characterCount).Contains("\n") == false)
                        {
                            lineBuilder.Append("\n");
                            continue;
                        }
                    }

                    lineBuilder.Append(currentLineNumber);
                    lineBuilder.Append('\n');

                    currentLineNumber++;

                    if (i - 1 == 0 && i - 1 < currentLineCount - 1)
                    {
                        int characterStart = inputText.textInfo.lineInfo[i - 1].firstCharacterIndex;
                        int characterCount = inputText.textInfo.lineInfo[i - 1].characterCount;

                        if (characterCount != 0 && inputText.text.Substring(characterStart, characterCount).Contains("\n") == false)
                        {
                            lineBuilder.Append("\n");
                            continue;
                        }
                    }
                }

                // Update displayed line numbers
                lineText.text = lineBuilder.ToString();
                lineCount = currentLineCount;
            }
        }

        private void UpdateCurrentLineColumnIndent()
        {
            // Get the current line number
            currentLine = inputText.textInfo.characterInfo[InputField.caretPosition].lineNumber;

            // Get the total character count
            int charCount = 0;
            for (int i = 0; i < currentLine; i++)
                charCount += inputText.textInfo.lineInfo[i].characterCount;

            // Get the column position
            currentColumn = inputField.caretPosition - charCount;

            currentIndent = 0;

            // Check for auto indent allowed
            if (languageTheme != null && languageTheme.autoIndent.allowAutoIndent == true)
            {
                for(int i = 0; i < inputField.caretPosition && i < inputField.text.Length; i++)
                {
                    // Get the character
                    char character = inputField.text[i];

                    // Check for opening indents
                    if (character == languageTheme.autoIndent.indentIncreaseCharacter)
                        currentIndent++;

                    // Check for closing indents
                    if (character == languageTheme.autoIndent.indentDecreaseCharacter)
                        currentIndent--;
                }

                // Dont allow negative indents
                if (currentIndent < 0)
                    currentIndent = 0;
            }
        }

        private void UpdateCurrentLineHighlight()
        {
            // Check if code editor is not active
            if (isActiveAndEnabled == false || lineHighlightLocked == true)
                return;

            int lineOffset = 0;

#if UNITY_2018_2_OR_NEWER
            if(applyLineOffsetFix == true)
                lineOffset++;
#endif

            // Highlight the current line
            lineHighlightTransform.anchoredPosition = new Vector2(5, inputText.textInfo.lineInfo[inputText.textInfo.characterInfo[0].lineNumber].lineHeight *
                (-inputText.textInfo.characterInfo[inputField.caretPosition].lineNumber + lineOffset) - 4 +
                inputTextTransform.anchoredPosition.y);
        }

        private string SyntaxHighlightContent(string inputText)
        {
            // Check if parsing should not run
            if (languageTheme == null)
                return inputText;

            // Check if the theme supports highlighting
            if (editorTheme != null && editorTheme.allowSyntaxHighlighting == false)
                return inputText;

            const string closingTag = "</color>";
            int offset = 0;

            highlightedBuilder.Length = 0;

            foreach (InputStringMatchInfo match in lexer.LexInputString(inputText))
            {
                // Copy text before the match
                for (int i = offset; i < match.startIndex; i++)
                    highlightedBuilder.Append(inputText[i]);

                // Add the opening color tag
                highlightedBuilder.Append(match.htmlColor);

                // Copy text inbetween the match boundaries
                for (int i = match.startIndex; i < match.endIndex; i++)
                    highlightedBuilder.Append(inputText[i]);

                // Add the closing color tag
                highlightedBuilder.Append(closingTag);

                // Update offset
                offset = match.endIndex;
            }

            // Copy remaining text
            for (int i = offset; i < inputText.Length; i++)
                highlightedBuilder.Append(inputText[i]);

            // Convert to string
            inputText = highlightedBuilder.ToString();

            return inputText;
        }

private void AutoIndentCaret(bool isClosingToken = false)
    {
        // 1. 엔터 키(Return) 입력 감지
        if (Input.GetKeyDown(KeyCode.Return) == true)
        {
            // 🔥 테마 파일(Python Syntax)이 여기서 이미 콜론(:)을 계산해서 currentIndent를 올려줍니다!
            UpdateCurrentLineColumnIndent();

            // (우리가 억지로 콜론을 세서 +1 하던 중복 로직은 삭제 완료!)

            string indent = GetAutoIndentTab(currentIndent);

            if (indent.Length > 0)
            {
                int insertPos = Mathf.Clamp(inputField.stringPosition, 0, inputField.text.Length);
                inputField.text = inputField.text.Insert(insertPos, indent);
                inputField.stringPosition = insertPos + indent.Length;
            }

            // 닫는 문자 처리
            bool immediateClosing = false;
            int closingOffset = -1;
            int checkPos = Mathf.Clamp(inputField.stringPosition, 0, inputField.text.Length);

            for (int i = checkPos; i < inputField.text.Length; i++)
            {
                if (inputField.text[i] == languageTheme.autoIndent.indentDecreaseCharacter)
                {
                    immediateClosing = true;
                    closingOffset = i - checkPos;
                    break;
                }
                if (char.IsWhiteSpace(inputField.text[i]) == false || inputField.text[i] == '\n')
                    break;
            }

            if (immediateClosing == true)
            {
                inputField.text = inputField.text.Remove(checkPos, closingOffset);
                string localIndent = (string.IsNullOrEmpty(indent) == true) ? string.Empty : indent;

                inputField.text = inputField.text.Insert(checkPos, GetAutoIndentTab(currentIndent) + "\n" + localIndent);
                UpdateCurrentLineColumnIndent();
            }

            inputText.text = inputField.text;
            inputText.SetText(inputField.text, true);
            inputText.Rebuild(CanvasUpdate.Prelayout);
            inputField.ForceLabelUpdate();
            inputField.Rebuild(CanvasUpdate.Prelayout);
            Refresh(true);
            delayedRefresh = true;
        }

        // 닫는 토큰 입력 시 들여쓰기 삭제 로직
        if (isClosingToken == true)
        {
            if (inputField.stringPosition >= 4) 
            {
                string lastFour = inputField.text.Substring(inputField.stringPosition - 4, 4);
                if (lastFour == "    ")
                {
                    inputField.text = inputField.text.Remove(inputField.stringPosition - 4, 4);
                    inputField.stringPosition = inputField.stringPosition - 4;
                }
            }
        }
    }
        private string GetAutoIndentTab(int amount)
        {
            string indentUnit = "    "; // 4칸 공백 (탭 대신 사용)
            string totalIndent = string.Empty;

            for (int i = 0; i < amount; i++)
                totalIndent += indentUnit;

            return totalIndent;
        }

        private void ApplyTheme()
        {
            // Check for missing references
            if (AllReferencesAssigned() == false)
                throw new MissingReferenceException("Cannot apply theme because one or more required component references are missing. Make sure all references under the 'Elements' header are assigned");

            bool nullTheme = false;

            // Check for no theme
            if (editorTheme == null)
            {
                // Get the default theme
                editorTheme = CodeEditorTheme.DefaultTheme;
                nullTheme = true;
            }

            // Apply theme colors
            inputField.caretColor = editorTheme.caretColor;
            //inputText.color = editorTheme.textColor;
			inputText.color = Color.clear;
            inputHighlightText.color = editorTheme.textColor;
            background.color = editorTheme.backgroundColor;
            lineHighlight.color = editorTheme.lineHighlightColor;
            lineNumberBackground.color = editorTheme.lineNumberBackgroundColor;
            lineText.color = editorTheme.lineNumberTextColor;
            scrollbar.color = editorTheme.scrollbarColor;

            // Set active to null
            if(nullTheme == true)
                editorTheme = null;
        }

        private void ApplyLanguage()
        {
            // Check for no theme
            char[] delimiters = null;
            MatchLexer[] matchers = null;

            // Get the matchers for the theme
            if (languageTheme != null)
            {
                delimiters = languageTheme.DelimiterSymbols;
                matchers = languageTheme.Matchers;
            }

            // Apply theme matchers
            lexer.UseMatchers(delimiters, matchers);
        }

        private bool AllReferencesAssigned()
        {
            if(inputField == null ||
                inputText == null ||
                inputHighlightText == null ||
                lineText == null ||
                background == null ||
                lineHighlight == null ||
                lineNumberBackground == null ||
                scrollbar == null)
            {
                // One or more references are not assigned
                return false;
            }
            return true;
        }
    }
}
