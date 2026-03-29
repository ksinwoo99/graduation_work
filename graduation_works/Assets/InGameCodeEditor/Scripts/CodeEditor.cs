using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using InGameCodeEditor.Lexer;
using System.Reflection;

namespace InGameCodeEditor
{
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

        public CodeEditorTheme EditorTheme
        {
            get { return editorTheme; }
            set { editorTheme = value; ApplyTheme(); }
        }

        public CodeLanguageTheme LanguageTheme
        {
            get { return languageTheme; }
            set { languageTheme = value; ApplyLanguage(); }
        }

        public TMP_InputField InputField { get { return inputField; } }
        public int LineCount { get { return lineCount; } }
        public int CurrentLine { get { return currentLine; } }
        public int CurrentColumn { get { return currentColumn; } }
        public int CurrentIndent { get { return currentIndent; } }

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

                    try
                    {
                        if(scrollBarUpdateFix == null)
                        {
                            scrollBarUpdateFix = typeof(TMP_InputField).GetMethod("UpdateScrollbar", BindingFlags.Instance | BindingFlags.NonPublic);
                        }
                        scrollBarUpdateFix.Invoke(inputField, null);
                    }
                    catch { }

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

        public string HighlightedText { get { return inputHighlightText.text; } }

        public bool LineNumbers
        {
            get { return lineNumbers; }
            set
            {
                lineNumbers = value;
                RectTransform inputFieldTransform = inputField.transform as RectTransform;
                RectTransform lineNumberBackgroudTransform = lineNumberBackground.transform as RectTransform;

                if (lineNumbers == true)
                {
                    lineNumberBackground.gameObject.SetActive(true);
                    lineText.gameObject.SetActive(true);
                    inputFieldTransform.offsetMin = new Vector2(lineNumbersSize, inputFieldTransform.offsetMin.y);
                    lineNumberBackgroudTransform.sizeDelta = new Vector2(lineNumbersSize + 15, lineNumberBackgroudTransform.sizeDelta.y);
                }
                else
                {
                    lineNumberBackground.gameObject.SetActive(false);
                    lineText.gameObject.SetActive(false);
                    inputFieldTransform.offsetMin = new Vector2(0, inputFieldTransform.offsetMin.y);
                }
            }
        }

        public int LineNumbersSize
        {
            get { return lineNumbersSize; }
            set { lineNumbersSize = value; LineNumbers = lineNumbers; }
        }

#if UNITY_EDITOR
        public void OnValidate()
        {
            LineNumbersSize = lineNumbersSize;
            if (AllReferencesAssigned() == true)
                if (editorTheme != null) ApplyTheme();
            if (languageTheme != null)
                languageTheme.Invalidate();
        }
#endif

        public void Awake()
        {
            if(AllReferencesAssigned() == false)
            {
                enabled = false;
                throw new MissingReferenceException("One or more required references are missing. Make sure all references under the 'Elements' header are assigned");
            }

            this.inputTextTransform = inputText.GetComponent<RectTransform>();
            this.lineHighlightTransform = lineHighlight.GetComponent<RectTransform>();
        }

        public void Start()
        {
            if (editorTheme == null) editorTheme = CodeEditorTheme.DefaultTheme;

            ApplyTheme();
            ApplyLanguage();

            if (inputField != null)
            {
                inputField.onValidateInput += (string text, int charIndex, char addedChar) => {
                    return addedChar == '\t' ? '\0' : addedChar;
                };
            }
        }

        public void LateUpdate()
        {
            if (Input.mouseScrollDelta != Vector2.zero || inputField.verticalScrollbar.value != lastScrollValue)
            {
                UpdateCurrentLineHighlight();
                lastScrollValue = inputField.verticalScrollbar.value;
            }

            if (inputField.isFocused == true)
            {
                inputHighlightText.text = inputField.text + Input.compositionString;
                inputField.textComponent.ForceMeshUpdate();
                inputHighlightText.ForceMeshUpdate();
                
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    int pos = inputField.stringPosition;
                    inputField.text = inputField.text.Insert(pos, "    ");
                    inputField.stringPosition = pos + 4;
                    inputField.ActivateInputField(); 
                    Refresh(true);
                }

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

                if (Input.GetKeyDown(KeyCode.Return))
                {
                    AutoIndentCaret(false);
                }
                else if (Input.anyKeyDown && Input.inputString.Contains(languageTheme.autoIndent.IndentDecreaseString))
                {
                    AutoIndentCaret(true);
                }
            }

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

                bool focusKeyPressed = false;
                foreach (KeyCode key in focusKeys)
                {
                    if (Input.GetKey(key)) { focusKeyPressed = true; break; }
                }

                if (focusKeyPressed || Input.GetMouseButton(0))
                    UpdateCurrentLineHighlight();
            }
        }

        public void Refresh(bool forceUpdate = false, bool updateLineOnly = true)
        {            
            DisplayedContentChanged(inputField.text, forceUpdate, updateLineOnly);
        }

        public void SetLineHighlight(int lineNumber, bool lockLineHighlight)
        {
            if (isActiveAndEnabled == false || lineNumber < 1 || lineNumber > LineCount)
                return;

            int lineOffset = 0;
            int lineIndex = lineNumber - 1;

#if UNITY_2018_2_OR_NEWER
            if(applyLineOffsetFix == true) lineOffset++;
#endif

            lineHighlightTransform.anchoredPosition = new Vector2(5, 
                (inputText.textInfo.lineInfo[inputText.textInfo.characterInfo[0].lineNumber].lineHeight *
                -lineIndex) + lineOffset - 4f +
                inputTextTransform.anchoredPosition.y);

            if (lockLineHighlight == true) LockLineHighlight();
            else UnlockLineHighlight();
        }

        public void LockLineHighlight() { lineHighlightLocked = true; }
        public void UnlockLineHighlight() { lineHighlightLocked = false; }

        private void DisplayedContentChanged(string newText, bool forceUpdate, bool updateLineOnly)
        {
            UpdateCurrentLineColumnIndent();

            if ((forceUpdate == false && lastText == newText) || string.IsNullOrEmpty(newText) == true)
            {
                if(string.IsNullOrEmpty(newText) == true) inputHighlightText.text = string.Empty;

                UpdateCurrentLineNumbers();
                UpdateCurrentLineHighlight();
                return;
            }

            inputHighlightText.text = SyntaxHighlightContent(newText);
            bool showScrollbar = inputField.verticalScrollbar.size < 1f;
            inputField.verticalScrollbar.gameObject.SetActive(showScrollbar);

            UpdateCurrentLineNumbers();
            UpdateCurrentLineHighlight();

            this.lastText = newText;
        }

        // ✨ [버그 픽스 1] 줄 번호가 1개 더 나오던 현상 완벽 수정
        private void UpdateCurrentLineNumbers()
        {
            // 텍스트 안의 엔터(\n) 개수를 직접 세어 정확한 논리적 줄 개수만 계산합니다.
            int exactLineCount = 1;
            for (int i = 0; i < inputField.text.Length; i++)
            {
                if (inputField.text[i] == '\n')
                    exactLineCount++;
            }

            // 개수가 바뀌었을 때만 UI를 새로 그립니다.
            if (exactLineCount != lineCount)
            {
                lineBuilder.Length = 0;
                for (int i = 1; i <= exactLineCount; i++)
                {
                    lineBuilder.Append(i);
                    lineBuilder.Append('\n');
                }

                lineText.text = lineBuilder.ToString();
                lineCount = exactLineCount;
            }
        }

        private void UpdateCurrentLineColumnIndent()
        {
            if (inputText.textInfo.characterInfo.Length <= inputField.caretPosition) return;
            
            currentLine = inputText.textInfo.characterInfo[inputField.caretPosition].lineNumber;

            int charCount = 0;
            for (int i = 0; i < currentLine; i++)
                charCount += inputText.textInfo.lineInfo[i].characterCount;

            currentColumn = inputField.caretPosition - charCount;
            currentIndent = 0;

            if (languageTheme != null && languageTheme.autoIndent.allowAutoIndent == true)
            {
                for(int i = 0; i < inputField.caretPosition && i < inputField.text.Length; i++)
                {
                    char character = inputField.text[i];
                    if (character == languageTheme.autoIndent.indentIncreaseCharacter) currentIndent++;
                    if (character == languageTheme.autoIndent.indentDecreaseCharacter) currentIndent--;
                }
                if (currentIndent < 0) currentIndent = 0;
            }
        }

        // ✨ [버그 픽스 2] 회색 하이라이트 박스 크기 동기화
        private void UpdateCurrentLineHighlight()
        {
            if (isActiveAndEnabled == false || lineHighlightLocked == true)
                return;

            int lineOffset = 0;

#if UNITY_2018_2_OR_NEWER
            if(applyLineOffsetFix == true) lineOffset++;
#endif

            float currentLineHeight = inputText.fontSize * 1.2f; // 텍스트가 비어있을 때 쓸 기본 높이
            int currentLineIdx = 0;

            // 텍스트가 1글자라도 있다면 TMP 렌더링 시스템에서 가장 정확한 실제 줄 높이를 가져옵니다.
            if (inputText.textInfo.characterCount > 0 && inputText.textInfo.lineCount > 0)
            {
                int charIdx = Mathf.Clamp(inputField.caretPosition, 0, inputText.textInfo.characterCount - 1);
                currentLineIdx = inputText.textInfo.characterInfo[charIdx].lineNumber;
                currentLineHeight = inputText.textInfo.lineInfo[currentLineIdx].lineHeight;
            }

            // ✨ 회색 바의 세로 크기(Y)를 현재 폰트 크기(currentLineHeight)에 딱 맞게 잡아 늘립니다!
            lineHighlightTransform.sizeDelta = new Vector2(lineHighlightTransform.sizeDelta.x, currentLineHeight);

            // ✨ 위치 또한 폰트 높이에 비례하도록 계산하여 스크롤 시 위아래로 어긋나는 현상 방지!
            lineHighlightTransform.anchoredPosition = new Vector2(5, 
                (currentLineHeight * (-currentLineIdx + lineOffset)) - (currentLineHeight * 0.1f) + 
                inputTextTransform.anchoredPosition.y);
        }

        private string SyntaxHighlightContent(string inputText)
        {
            if (languageTheme == null) return inputText;
            if (editorTheme != null && editorTheme.allowSyntaxHighlighting == false) return inputText;

            const string closingTag = "</color>";
            int offset = 0;

            highlightedBuilder.Length = 0;

            foreach (InputStringMatchInfo match in lexer.LexInputString(inputText))
            {
                for (int i = offset; i < match.startIndex; i++) highlightedBuilder.Append(inputText[i]);
                highlightedBuilder.Append(match.htmlColor);
                for (int i = match.startIndex; i < match.endIndex; i++) highlightedBuilder.Append(inputText[i]);
                highlightedBuilder.Append(closingTag);
                offset = match.endIndex;
            }

            for (int i = offset; i < inputText.Length; i++) highlightedBuilder.Append(inputText[i]);

            inputText = highlightedBuilder.ToString();
            return inputText;
        }

        private void AutoIndentCaret(bool isClosingToken = false)
        {
            if (Input.GetKeyDown(KeyCode.Return) == true)
            {
                UpdateCurrentLineColumnIndent();

                string indent = GetAutoIndentTab(currentIndent);

                if (indent.Length > 0)
                {
                    int insertPos = Mathf.Clamp(inputField.stringPosition, 0, inputField.text.Length);
                    inputField.text = inputField.text.Insert(insertPos, indent);
                    inputField.stringPosition = insertPos + indent.Length;
                }

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
                    if (char.IsWhiteSpace(inputField.text[i]) == false || inputField.text[i] == '\n') break;
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
            string indentUnit = "    "; 
            string totalIndent = string.Empty;

            for (int i = 0; i < amount; i++) totalIndent += indentUnit;
            return totalIndent;
        }

        private void ApplyTheme()
        {
            if (AllReferencesAssigned() == false)
                throw new MissingReferenceException("Cannot apply theme because one or more required component references are missing.");

            bool nullTheme = false;

            if (editorTheme == null)
            {
                editorTheme = CodeEditorTheme.DefaultTheme;
                nullTheme = true;
            }

            inputField.caretColor = editorTheme.caretColor;
            inputText.color = Color.clear;
            inputHighlightText.color = editorTheme.textColor;
            background.color = editorTheme.backgroundColor;
            lineHighlight.color = editorTheme.lineHighlightColor;
            lineNumberBackground.color = editorTheme.lineNumberBackgroundColor;
            lineText.color = editorTheme.lineNumberTextColor;
            scrollbar.color = editorTheme.scrollbarColor;

            if(nullTheme == true) editorTheme = null;
        }

        private void ApplyLanguage()
        {
            char[] delimiters = null;
            MatchLexer[] matchers = null;

            if (languageTheme != null)
            {
                delimiters = languageTheme.DelimiterSymbols;
                matchers = languageTheme.Matchers;
            }

            lexer.UseMatchers(delimiters, matchers);
        }

        private bool AllReferencesAssigned()
        {
            if(inputField == null || inputText == null || inputHighlightText == null ||
               lineText == null || background == null || lineHighlight == null ||
               lineNumberBackground == null || scrollbar == null) return false;
            return true;
        }
    }
}