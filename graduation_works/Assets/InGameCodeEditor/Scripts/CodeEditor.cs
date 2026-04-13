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
        // IME 조합 삽입 위치 추적 ─────────────────────────────
        // _imeAnchorPos     : 현재 조합이 삽입될 문자열 위치
        // _imeAnchorTextLen : 앵커를 기록한 시점의 inputField.text 길이
        //
        // text 길이가 변했다 = 이전 음절이 확정(commit)됐다는 신호입니다.
        // 이 경우 앵커를 재계산해야 올바른 위치에 다음 음절 조합이 표시됩니다.
        private int _imeAnchorPos     = -1;
        private int _imeAnchorTextLen = -1;

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
                    if (addedChar == '\t') return '\0';
                    // Enter 키의 \n 은 TMP 내부가 아닌 우리 Update()가 직접 삽입합니다.
                    // Input.GetKeyDown 으로 판별하면 TMP/EventSystem 실행 순서에 무관하게
                    // 중복 삽입을 방지할 수 있습니다 (suppressNextNewline 플래그 방식의
                    // 타이밍 취약점을 대체합니다).
                    if (addedChar == '\n' &&
                        (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                        return '\0';
                    return addedChar;
                };
            }
        }

        public void Update()
        {
            if (inputField == null || !inputField.isFocused) return;

            // IME 조합 중(한글 자음·모음 결합 진행 중)에는 커스텀 키 처리를 건너뜁니다.
            // Return/Backspace 핸들러가 조합 진행 중인 글자에 끼어드는 현상을 방지합니다.
            if (!string.IsNullOrEmpty(Input.compositionString)) return;

            // Return / KeypadEnter: \n + 들여쓰기를 직접 삽입합니다.
            // TMP의 \n 삽입은 onValidateInput의 Input.GetKeyDown 검사로 차단됩니다.
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                int pos = inputField.stringPosition;
                string text = inputField.text;

                // 커서 앞 현재 줄 텍스트 추출
                int lastNewlineIdx = pos > 0 ? text.LastIndexOf('\n', pos - 1) : -1;
                string currentLineText = lastNewlineIdx >= 0
                    ? text.Substring(lastNewlineIdx + 1, pos - lastNewlineIdx - 1)
                    : text.Substring(0, pos);

                // 앞쪽 공백 수 계산 (기존 들여쓰기 유지)
                int spaceCount = 0;
                while (spaceCount < currentLineText.Length && currentLineText[spaceCount] == ' ')
                    spaceCount++;

                // :, (, [, { 로 끝나면 4칸 추가 들여쓰기 (새 블록 진입)
                string trimmed = currentLineText.TrimEnd();
                if (trimmed.EndsWith(":") || trimmed.EndsWith("(") ||
                    trimmed.EndsWith("[") || trimmed.EndsWith("{"))
                    spaceCount += 4;

                string indent = new string(' ', spaceCount);

                // \n + 들여쓰기 직접 삽입
                inputField.text = text.Insert(pos, "\n" + indent);
                inputField.stringPosition = pos + 1 + indent.Length;
                inputField.ForceLabelUpdate();

                Refresh(true);
                delayedRefresh = true;
            }

            // Backspace 의 4칸 단위 들여쓰기 삭제는 LateUpdate()에서 처리합니다.
            // (TMP가 EventSystem.Update 에서 Backspace를 먼저 처리한 뒤,
            //  우리 LateUpdate에서 나머지를 보상 삭제하는 방식)
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
                // IME 조합 중인 글자(compositionString)를 올바른 커서 위치에 삽입해 렌더링합니다.
                //
                // [문제1] inputField.stringPosition 은 TMP LateUpdate 실행 후
                //         조합 글자 길이만큼 이동할 수 있어 커서 중간에서는 신뢰할 수 없습니다.
                // [문제2] 한글은 음절 단위로 확정(commit)되므로, 음절이 바뀔 때
                //         compositionString 이 비어지는 프레임 없이 곧바로 다음 조합으로
                //         전환됩니다. 이때 앵커를 갱신하지 않으면 새 음절이 앞에 붙습니다.
                //
                // [해결]  ① 조합이 시작될 때 stringPosition 을 _imeAnchorPos 에 저장합니다.
                //         ② text 길이가 바뀌면(= 음절 확정) 앵커를 즉시 재계산합니다.
                //         ③ 커서가 텍스트 끝에 있을 때는 stringPosition 대신
                //            text.Length 를 사용해 TMP 내부 이동의 영향을 피합니다.
                string _comp   = Input.compositionString;
                int    _curLen = inputField.text.Length;

                if (!string.IsNullOrEmpty(_comp))
                {
                    // 앵커 갱신 조건: 첫 조합 프레임 OR 음절 확정으로 텍스트 길이 변경
                    bool needsReset = _imeAnchorPos < 0 || _curLen != _imeAnchorTextLen;
                    if (needsReset)
                    {
                        int sp = inputField.stringPosition;
                        // 커서가 텍스트 끝이면 text.Length 를 직접 사용 (TMP 이동 영향 없음)
                        // 커서가 중간이면 stringPosition 을 사용 (확정 직후라 신뢰 가능)
                        _imeAnchorPos     = (sp >= _curLen) ? _curLen : sp;
                        _imeAnchorTextLen = _curLen;
                    }

                    int insertAt = Mathf.Clamp(_imeAnchorPos, 0, _curLen);
                    inputHighlightText.text = inputField.text.Substring(0, insertAt)
                                           + _comp
                                           + inputField.text.Substring(insertAt);
                }
                else
                {
                    // 조합 완전 종료 — 모두 리셋
                    _imeAnchorPos     = -1;
                    _imeAnchorTextLen = -1;
                    inputHighlightText.text = inputField.text;
                }
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

                // ── Backspace 4칸 단위 들여쓰기 삭제 (블록 탈출) ─────────────
                // TMP가 EventSystem.Update에서 Backspace 1칸을 이미 삭제한 뒤
                // 우리 LateUpdate가 실행되므로, 남은 공백을 보상 삭제합니다.
                //
                // 동작 규칙:
                //   줄 시작부터 커서까지 모두 공백이고(순수 들여쓰기 줄),
                //   현재 공백 수가 4의 배수가 아니면(TMP가 1칸 지워 어긋난 상태)
                //   이전 4칸 경계까지 나머지를 추가 삭제합니다.
                //   → 결과적으로 Backspace 한 번에 정확히 4칸이 제거됩니다.
                if (Input.GetKeyDown(KeyCode.Backspace) && string.IsNullOrEmpty(Input.compositionString))
                {
                    int bsPos  = inputField.stringPosition;
                    string bsText = inputField.text;

                    // 줄 시작 위치 탐색
                    int bsLineStart = bsPos;
                    while (bsLineStart > 0 && bsText[bsLineStart - 1] != '\n')
                        bsLineStart--;

                    // 줄 시작부터 커서까지 연속 공백 수 계산
                    int spaces = 0;
                    bool onlySpaces = true;
                    for (int i = bsLineStart; i < bsPos; i++)
                    {
                        if (bsText[i] == ' ') spaces++;
                        else { onlySpaces = false; break; }
                    }

                    // 순수 들여쓰기 줄이고 4의 배수가 아니면 나머지 보상 삭제
                    if (onlySpaces && spaces > 0 && spaces % 4 != 0)
                    {
                        int extra = spaces % 4;
                        inputField.text = bsText.Remove(bsPos - extra, extra);
                        inputField.stringPosition = bsPos - extra;
                        Refresh(true);
                    }
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

                // IME 조합 중에는 Refresh를 건너뜁니다.
                // 한글 한 글자를 입력할 때 자음·모음 각각의 키마다 Refresh가 호출되면
                // 불필요한 신택스 하이라이팅 재계산이 반복되어 입력 지연이 생깁니다.
                // 조합이 완료되어 글자가 확정(commit)된 시점에 한 번만 Refresh됩니다.
                if (Input.anyKey && string.IsNullOrEmpty(Input.compositionString)) Refresh();

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