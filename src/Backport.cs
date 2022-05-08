/*
 * Bridge between Unity and Risa (C99). Designed for in-game consoles. 
 */
using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Before the event system
[DefaultExecutionOrder(-10000)]
public class Backport : MonoBehaviour
{
    public enum State
    {
        IDLE,
        VM_RUNNING,
        VM_RUNNING_AUTOEXEC
    }

    // If the VM enters an infinite loop, don't freeze the game.
    public static uint MAX_INSTRUCTIONS_PER_FRAME = 100;
    // The number of commands to keep in the history buffer. Keep this a small number like 5 (otherwise major slowdowns may occur).
    public static uint MAX_HISTORY_SIZE = 5;
    // If the command result is a function, execute that function automatically (so the user can simply write "clear").
    public static bool AUTOEXEC_ENABLE = true;
    // If a command result was executed as a function, and it returned another function, execute again.
    public static bool AUTOEXEC_REPEAT = true;

    // Show/hide the console.
    public const KeyCode CONSOLE_TOGGLE_KEY = KeyCode.BackQuote;
    // Force stop the VM (like CTRL+C in a terminal emulator)
    public const KeyCode INTERRUPT_VM_KEY = KeyCode.Escape;

    public static Backport INSTANCE;

    [SerializeField]
    GameObject canvas;
    [SerializeField]
    TMP_InputField output;
    [SerializeField]
    TMP_InputField input;
    [SerializeField]
    GameObject inputPrefix;

    [NonSerialized]
    public Risa.VM vm;

    State state;
    int lineCount;

    LinkedList<string> history;
    LinkedListNode<string> currentHistoryNode;

    void Awake()
    {
        if(INSTANCE != null)
        {
            DestroyImmediate(gameObject);
            return;
        }

        INSTANCE = this;

        vm = new Risa.VM(true);
        vm.GetIO().RedirectIn(Stdin);
        vm.GetIO().RedirectOut(Stdout);
        vm.GetIO().RedirectErr(Stderr);

        vm.LoadAllLibraries();

        state = State.IDLE;

        // Enter -> execute
        input.onSubmit.AddListener((text) =>
        {
            if(string.IsNullOrWhiteSpace(text))
            {
                input.text = "";
                input.ActivateInputField();
                return;
            }

            // Add the command to the history buffer. If it exists, move it to the head of the list
            LinkedListNode<string> it = history.Count > 0 ? history.First : null;

            while(it != null)
            {
                if(text == it.Value)
                {
                    history.Remove(it);
                    break;
                }

                it = it.Next;
            }

            history.AddFirst(text);
            currentHistoryNode = null;

            if (history.Count > MAX_HISTORY_SIZE)
            {
                history.RemoveLast();
            }

            if (!text.EndsWith(';'))
            {
                // In case the user forgets the semicolon.
                // Even if Risa expects a semicolon, it's a chore for the user to write it every single time.
                // Expressions like "1 + 1" are much easier to write without a trailing semicolon.
                text = text + ';';
            }

            if (state == State.IDLE)
            {
                OnExecutionStart();
                if (!Execute(text))
                {
                    OnExecutionEnd(false);
                }
            }
        });

        Risa.ValueObject backport = vm.CreateObject();
        backport.Set("max_instructions", vm.CreateNative(MaxInstructions));
        backport.Set("history_size", vm.CreateNative(HistorySize));
        backport.Set("line_limit", vm.CreateNative(LineLimit));
        backport.Set("autoexec", vm.CreateNative(Autoexec));
        backport.Set("autoexec_repeat", vm.CreateNative(AutoexecRepeat));
        backport.Set("version", vm.CreateNative(Version));

        vm.LoadGlobal("backport", backport.ToValue());
        vm.LoadGlobalNative("clear", Clear);

        BackportCommands.Init();

        history = new LinkedList<string>();
        currentHistoryNode = null;

        lineCount = 1;
        WriteVersion();

        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        HandleInput();

        switch(state)
        {
            case State.IDLE:
                break;
            case State.VM_RUNNING:
            case State.VM_RUNNING_AUTOEXEC:
                try
                {
                    if (vm.Run(MAX_INSTRUCTIONS_PER_FRAME))
                    {
                        OnExecutionEnd(true);
                    }
                }
                catch(Risa.RuntimeException)
                {
                    OnExecutionEnd(false);
                }
                break;
        }
    }

    void HandleInput()
    {
        if(GetKeyDownNoMod(CONSOLE_TOGGLE_KEY))
        {
            if(!canvas.activeSelf)
            {
                canvas.SetActive(true);
                input.Select();
                input.ActivateInputField();
            }
            else
            {
                canvas.SetActive(false);
            }
        }
        else if(state == State.VM_RUNNING && IsOpen() && GetKeyDownNoMod(INTERRUPT_VM_KEY))
        {
            state = State.IDLE;
            WriteError("-- interrupted --");
            OnExecutionEnd(false);
        }
        else if(IsOpen() && input.isActiveAndEnabled && input.isFocused)
        {
            if (history.Count == 0)
            {
                return;
            }

            if (GetKeyDownNoMod(KeyCode.UpArrow))
            {
                if (currentHistoryNode == null || currentHistoryNode.Value != input.text)
                {
                    currentHistoryNode = history.First;
                }
                else
                {
                    currentHistoryNode = currentHistoryNode.Next != null ? currentHistoryNode.Next : history.First;
                }

                input.text = currentHistoryNode.Value;
            }
            else if(GetKeyDownNoMod(KeyCode.DownArrow))
            {
                if (currentHistoryNode == null || currentHistoryNode.Value != input.text)
                {
                    currentHistoryNode = history.Last;
                }
                else
                {
                    currentHistoryNode = currentHistoryNode.Previous != null ? currentHistoryNode.Previous : history.Last;
                }

                input.text = currentHistoryNode.Value;
            }
        }
    }

    void OnExecutionStart()
    {
        input.readOnly = true;
        inputPrefix.SetActive(false);

        if(!output.text.EndsWith("\n"))
        {
            WriteLine("");
        }

        WriteLine(string.Format("> {0}", input.text));

        input.text = "";
    }

    void OnExecutionEnd(bool success)
    {
        State oldState = state;

        if(state == State.VM_RUNNING_AUTOEXEC)
        {
            vm.LoadGlobal("__backport_autoexec", Risa.Value.NULL);
        }

        state = State.IDLE;

        if (success)
        {
            Risa.Value result = vm.GetLastResult();
            string resultStr = vm.GetLastResult().ToString();

            // The result is a native function, autoexec is enabled, and autoexec can repeat or the last executed function wasn't autoexec.
            if(result.IsNative() && AUTOEXEC_ENABLE && (AUTOEXEC_REPEAT || oldState != State.VM_RUNNING_AUTOEXEC))
            {
                vm.LoadGlobal("__backport_autoexec", result);

                if (Execute("__backport_autoexec();"))
                {
                    return;
                }
            }

            // Print the result, if any.
            // "null" as a string, and not as a keyword, since it's a Risa null.
            if (resultStr != "null")
            {
                WriteLine(resultStr);
            }
        }

        input.readOnly = false;
        input.ActivateInputField();
        inputPrefix.SetActive(true);
    }

    public static bool IsOpen()
    {
        Check();

        return INSTANCE.canvas.activeSelf;
    }

    public bool Execute(string source)
    {
        Debug.Assert(state == State.IDLE, "[BACKPORT] Execute() called when the VM is already running");

        try
        {
            vm.Load(source);
            state = State.VM_RUNNING;
            return true;
        }
        catch(Risa.CompileTimeException)
        {
            return false;
        }
    }

    public static void ClearOutput()
    {
        Check();
        INSTANCE.lineCount = 1;
        SetOutput("");
    }

    // This doesn't update lineCount because it was already updated by Write/ClearOutput
    // Therefore, don't use this function directly
    static void SetOutput(string text)
    {
        bool scrollToBottom = (INSTANCE.output.verticalScrollbar.value == 1f || INSTANCE.output.verticalScrollbar.size == 1f);

        INSTANCE.output.text = text; // <--- this updates the scrollbar fields

        // Scroll if the scrollbar was already scrolled to the max before, or if there was no space to scroll before (and now there is)
        if (INSTANCE.output.verticalScrollbar.size != 1f)
        {
            if (scrollToBottom)
            {
                INSTANCE.output.verticalScrollbar.value = 1f;
            }

            if (!INSTANCE.output.verticalScrollbar.gameObject.activeSelf)
            {
                // There's now space to scroll, show the scrollbar
                INSTANCE.output.verticalScrollbar.gameObject.SetActive(true);
            }
        }
        else
        {
            INSTANCE.output.verticalScrollbar.value = 0;

            // There's no space to scroll, hide the scrollbar
            if (INSTANCE.output.verticalScrollbar.gameObject.activeSelf)
            {
                INSTANCE.output.verticalScrollbar.gameObject.SetActive(false);
            }
        }
    }

    public static void Write(string text)
    {
        Check();

        // First, ensure that the line limit is satisfied
        bool hasNewLines = false;

        for (int i = 0; i < text.Length; ++i)
        {
            if(text[i] == '\n')
            {
                ++INSTANCE.lineCount;
                hasNewLines = true;
            }
        }

        // Limit exceeded, try to delete old lines
        if(hasNewLines && INSTANCE.lineCount > INSTANCE.output.lineLimit)
        {
            DeleteOldOutputLines(-1);

            // If it's still above the limit, deleting old lines isn't enough
            // Therefore, delete from the text lines
            if (INSTANCE.lineCount > INSTANCE.output.lineLimit)
            {
                for (int i = 0; i < text.Length; ++i)
                {
                    if (text[i] == '\n')
                    {
                        if (--INSTANCE.lineCount == INSTANCE.output.lineLimit)
                        {
                            text = text.Substring(i + 1);
                            break;
                        }
                    }
                }
            }
        }

        SetOutput(INSTANCE.output.text + text);
    }

    public static void WriteLine(string text)
    {
        Write(text + "\n");
    }

    public static void WriteError(string text)
    {
        WriteLine(string.Format("<color=#FF0000>{0}</color>", text));
    }

    static void WriteVersion()
    {
        WriteLine(string.Format("Backport ver1.0 (risa {0})\n(C) 2022 The Deprimus Members\n", Risa.C99.VERSION));
    }

    static string Stdin(Risa.IO.InputMode mode)
    {
        return "";
    }

    static void Stdout(string msg)
    {
        Write(msg);
    }

    static void Stderr(string msg)
    {
        WriteError(msg.Trim('\n'));
    }

    static void Check()
    {
        Debug.Assert(INSTANCE != null, "[BACKPORT] Backport was not initialized, but a backport method was called");
    }

    static bool GetKeyDownNoMod(KeyCode key)
    {
        return Input.GetKeyDown(key)
            && !Input.GetKeyDown(KeyCode.LeftShift)
            && !Input.GetKeyDown(KeyCode.RightShift)
            && !Input.GetKeyDown(KeyCode.LeftControl)
            && !Input.GetKeyDown(KeyCode.RightControl)
            && !Input.GetKeyDown(KeyCode.LeftAlt)
            && !Input.GetKeyDown(KeyCode.RightAlt)
            && !Input.GetKeyDown(KeyCode.LeftMeta)
            && !Input.GetKeyDown(KeyCode.RightMeta);
    }

    static void DeleteOldOutputLines(int limit)
    {
        if(limit == -1)
        {
            limit = INSTANCE.output.lineLimit;
        }

        // Find the first index from which to substring, such that the line count doesn't exceed the limit
        for (int i = 0; i < INSTANCE.output.text.Length; ++i)
        {
            if (INSTANCE.output.text[i] == '\n')
            {
                if (--INSTANCE.lineCount == limit)
                {
                    INSTANCE.output.text = INSTANCE.output.text.Substring(i + 1);
                    break;
                }
            }
        }
    }

    static Risa.Value MaxInstructions(Risa.VM vm, Risa.Args args)
    {
        if (args.Count() == 0)
        {
            return vm.CreateInt(MAX_INSTRUCTIONS_PER_FRAME);
        }

        if (args.Count() > 1 || !args.Get(0).IsInt())
        {
            WriteError("Invalid arguments for max_instructions (expected an int >= 0)");
            return Risa.Value.NULL;
        }

        int value = (int)args.Get(0).AsInt();

        if (value < 0)
        {
            WriteError("Invalid arguments for max_instructions (expected an int >= 0)");
            return Risa.Value.NULL;
        }

        MAX_INSTRUCTIONS_PER_FRAME = (uint)value;
        return vm.CreateInt(MAX_INSTRUCTIONS_PER_FRAME);
    }

    static Risa.Value HistorySize(Risa.VM vm, Risa.Args args)
    {
        if (args.Count() == 0)
        {
            return vm.CreateInt(MAX_HISTORY_SIZE);
        }

        if (args.Count() > 1 || !args.Get(0).IsInt())
        {
            WriteError("Invalid arguments for history_size (expected an int > 0)");
            return Risa.Value.NULL;
        }

        int value = (int)args.Get(0).AsInt();

        if (value <= 0)
        {
            WriteError("Invalid arguments for history_size (expected an int > 0)");
            return Risa.Value.NULL;
        }

        // Ensure that the history size doesn't exceed the limit
        while (INSTANCE.history.Count > value)
        {
            INSTANCE.history.RemoveLast();
        }

        MAX_HISTORY_SIZE = (uint)value;
        return vm.CreateInt(MAX_HISTORY_SIZE);
    }

    static Risa.Value LineLimit(Risa.VM  vm, Risa.Args args)
    {
        if(args.Count() == 0)
        {
            return vm.CreateInt(INSTANCE.output.lineLimit);
        }

        if(args.Count() > 1 || !args.Get(0).IsInt())
        {
            WriteError("Invalid arguments for line_limit (expected an int > 0)");
            return Risa.Value.NULL;
        }

        int value = (int)args.Get(0).AsInt();

        if(value <= 0)
        {
            WriteError("Invalid arguments for line_limit (expected an int > 0)");
            return Risa.Value.NULL;
        }

        // Ensure that the content doesn't exceed the limit
        if (INSTANCE.lineCount > value)
        {
            DeleteOldOutputLines(value);
        }

        INSTANCE.output.lineLimit = value;
        return vm.CreateInt(INSTANCE.output.lineLimit);
    }

    static Risa.Value Autoexec(Risa.VM vm, Risa.Args args)
    {
        if (args.Count() == 0)
        {
            return vm.CreateBool(AUTOEXEC_ENABLE);
        }

        if (args.Count() > 1 || !args.Get(0).IsBool())
        {
            WriteError("Invalid arguments for autoexec (expected a bool)");
            return Risa.Value.NULL;
        }

        bool value = args.Get(0).AsBool();

        AUTOEXEC_ENABLE = value;
        return vm.CreateBool(AUTOEXEC_ENABLE);
    }

    static Risa.Value AutoexecRepeat(Risa.VM vm, Risa.Args args)
    {
        if (args.Count() == 0)
        {
            return vm.CreateBool(AUTOEXEC_REPEAT);
        }

        if (args.Count() > 1 || !args.Get(0).IsBool())
        {
            WriteError("Invalid arguments for autoexec_repeat (expected a bool)");
            return Risa.Value.NULL;
        }

        bool value = args.Get(0).AsBool();

        AUTOEXEC_REPEAT = value;
        return vm.CreateBool(AUTOEXEC_REPEAT);
    }

    static Risa.Value Clear(Risa.VM vm, Risa.Args args)
    {
        if(args.Count() > 0)
        {
            WriteError("Expected no arguments for clear");
            return Risa.Value.NULL;
        }

        ClearOutput();
        return Risa.Value.NULL;
    }

    static Risa.Value Version(Risa.VM vm, Risa.Args args)
    {
        if (args.Count() > 0)
        {
            WriteError("Expected no arguments for version");
            return Risa.Value.NULL;
        }

        WriteVersion();
        return Risa.Value.NULL;
    }
}
