﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BomberEngine.Core;
using BomberEngine.Core.Visual;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using BomberEngine.Core.Input;
using Microsoft.Xna.Framework.Input;
using BomberEngine.Debugging.Commands;
using BomberEngine.Game;
using BomberEngine.Core.Assets.Types;
using BomberEngine.Core.Events;

namespace BomberEngine.Debugging
{
    public class GameConsole : Screen
    {
        private List<String> m_lines;

        private const String PROMPT_STRING = "> ";
        private StringBuilder commandBuffer;

        private int cursorPos;

        private Font font;
        private float charWidth;
        private float lineHeight;
        private float lineSpacing;

        private bool shiftPressed;

        private HashSet<KeyCode> additionalInputKeys;

        private ConsoleCommandRegister commands;
        private LinkedList<ConsoleCommand> suggestedCommands;

        private Color backColor;

        public GameConsole(Font font)
            : base(640, 320)
        {
            this.font = font;

            AllowsDrawPrevious = true;
            AllowsUpdatePrevious = true;

            m_lines = new List<String>();
            commands = new ConsoleCommandRegister();

            commandBuffer = new StringBuilder();
            suggestedCommands = new LinkedList<ConsoleCommand>();

            charWidth = font.StringWidth("W");
            lineHeight = font.FontHeight();
            InitAdditionalInputKeys();

            backColor = new Color(0.0f, 0.0f, 0.0f, 0.75f);
        }

        private void InitAdditionalInputKeys()
        {
            additionalInputKeys = new HashSet<KeyCode>();
            additionalInputKeys.Add(KeyCode.KB_Space);
            additionalInputKeys.Add(KeyCode.KB_Multiply);
            additionalInputKeys.Add(KeyCode.KB_Add);
            additionalInputKeys.Add(KeyCode.KB_Separator);
            additionalInputKeys.Add(KeyCode.KB_Subtract);
            additionalInputKeys.Add(KeyCode.KB_Decimal);
            additionalInputKeys.Add(KeyCode.KB_Divide);
            additionalInputKeys.Add(KeyCode.KB_OemSemicolon);
            additionalInputKeys.Add(KeyCode.KB_OemPlus);
            additionalInputKeys.Add(KeyCode.KB_OemComma);
            additionalInputKeys.Add(KeyCode.KB_OemMinus);
            additionalInputKeys.Add(KeyCode.KB_OemPeriod);
            additionalInputKeys.Add(KeyCode.KB_OemQuestion);
            additionalInputKeys.Add(KeyCode.KB_OemOpenBrackets);
            additionalInputKeys.Add(KeyCode.KB_OemPipe);
            additionalInputKeys.Add(KeyCode.KB_OemCloseBrackets);
            additionalInputKeys.Add(KeyCode.KB_OemQuotes);
            additionalInputKeys.Add(KeyCode.KB_OemBackslash);
        }

        public bool RegisterCommand(ConsoleCommand command)
        {
            return commands.RegisterCommand(command);
        }

        public override void Draw(Context context)
        {
            context.FillRect(0, 0, width, height, backColor);

            DrawLines(context);
            DrawPrompt(context);
        }

        private void DrawLines(Context context)
        {
            float drawX = 10;
            float drawY = height - 2 * (lineHeight + lineSpacing) - 10;

            for (int i = m_lines.Count - 1; i >= 0; --i)
            {
                String line = m_lines[i];

                font.DrawString(context, line, drawX, drawY);
                drawY -= lineHeight + lineSpacing;

                if (drawY < 0)
                {
                    break;
                }
            }
        }

        private void DrawPrompt(Context context)
        {
            float drawX = 10;
            float drawY = height - lineHeight - lineSpacing - 10;

            font.DrawString(context, PROMPT_STRING, drawX, drawY);
            drawX += PROMPT_STRING.Length * charWidth;

            context.FillRect(drawX + cursorPos * charWidth, drawY + lineHeight, charWidth, 3, Color.White);
            font.DrawString(context, commandBuffer.ToString(), drawX, drawY);
        }

        public void AddLine(String line)
        {
            m_lines.Add(line);
        }

        private void EnterChar(char chr)
        {
            commandBuffer.Insert(cursorPos++, chr);
        }

        private void SetCommandText(String text)
        {
            SetCommandText(text, false);
        }

        private void SetCommandText(String text, bool addSpace)
        {
            commandBuffer.Clear();
            commandBuffer.Append(text);
            if (addSpace)
            {
                commandBuffer.Append(" ");
            }

            cursorPos = commandBuffer.Length;
        }

        private void DeleteChar()
        {
            if (cursorPos > 0)
            {
                commandBuffer.Remove(--cursorPos, 1);
            }
        }

        private void TryExecuteCommand()
        {
            String commandString = commandBuffer.ToString();
            String[] tokens = commandString.Split(' ');

            if (tokens.Length > 0)
            {
                String name = tokens[0];
                ConsoleCommand command = commands.FindCommand(name);
                if (command != null)
                {
                    if (tokens.Length > 1)
                    {
                        String[] args = new String[tokens.Length - 1];
                        Array.Copy(tokens, 1, args, 0, args.Length);

                        command.Execute(this, args);
                    }
                    else
                    {
                        command.Execute(this);
                    }
                }
                else
                {
                    AddLine("Unknown command: '" + commandString + "'");
                }
            }

            SetCommandText("");
        }

        private void DoAutoComplete()
        {
            String token = commandBuffer.ToString();
            if (token.Length > 0)
            {
                suggestedCommands.Clear();
                commands.GetSuggested(token, suggestedCommands);

                if (suggestedCommands.Count == 1)
                {
                    ConsoleCommand command = suggestedCommands.First.Value;
                    SetCommandText(command.GetName(), true);
                }
                else if (suggestedCommands.Count > 1)
                {
                    String suggestedText = GetSuggestedText(token, suggestedCommands);
                    SetCommandText(suggestedText, false);
                }
            }
        }

        private String GetSuggestedText(String token, LinkedList<ConsoleCommand> commandList)
        {
            LinkedListNode<ConsoleCommand> firstNode = commandList.First;
            String firstCommandName = firstNode.Value.GetName();

            if (firstCommandName.Length > token.Length)
            {
                StringBuilder suggestedToken = new StringBuilder(token);
                for (int i = token.Length; i < firstCommandName.Length; ++i)
                {
                    char chr = firstCommandName[i];
                    for (LinkedListNode<ConsoleCommand> nextNode = firstNode.Next; nextNode != null; nextNode = nextNode.Next)
                    {
                        String otherCommandName = nextNode.Value.GetName();
                        if (otherCommandName[i] != chr)
                        {
                            return suggestedToken.ToString();
                        }
                    }
                    suggestedToken.Append(chr);
                }

                return suggestedToken.ToString();
            }

            return token;
        }

        private void Clear()
        {
            commandBuffer.Clear();
            cursorPos = 0;
        }

        private void MoveCursorLeft()
        {
            if (cursorPos > 0)
            {
                --cursorPos;
            }
        }

        private void MoveCursorRight()
        {
            if (cursorPos < commandBuffer.Length)
            {
                ++cursorPos;
            }
        }

        public List<String> lines
        {
            get { return m_lines; }
        }

        public override bool HandleEvent(Event evt)
        {
            if (evt.code == Event.KEY)
            {
                KeyEvent keyEvent = evt as KeyEvent;
                KeyCode key = keyEvent.arg.key;

                switch (keyEvent.state)
                {
                    case KeyEvent.PRESSED:
                    {
                        if (OnKeyPressed(key)) return true;
                        break;
                    }

                    case KeyEvent.REPEATED:
                    {
                        if (OnKeyRepeat(key)) return true;
                        break;
                    }

                    case KeyEvent.RELEASED:
                    {
                        if (OnKeyReleased(key)) return true;
                        break;
                    }
                }
            }

            return base.HandleEvent(evt);
        }

        private bool OnKeyPressed(KeyCode key)
        {
            if (key >= KeyCode.KB_A && key <= KeyCode.KB_Z)
            {
                char chr = (char)key;
                if (!shiftPressed)
                {
                    chr = char.ToLower(chr);
                }

                EnterChar(chr);
                return true;
            }

            if (key >= KeyCode.KB_D0 && key <= KeyCode.KB_D9 || key >= KeyCode.KB_NumPad0 && key <= KeyCode.KB_NumPad9)
            {
                EnterChar((char)key);
                return true;
            }

            if (key == KeyCode.KB_Left)
            {
                MoveCursorLeft();
                return true;
            }

            if (key == KeyCode.KB_Right)
            {
                MoveCursorRight();
                return true;
            }

            if (key == KeyCode.KB_Back)
            {
                DeleteChar();
                return true;
            }

            if (key == KeyCode.KB_Enter)
            {
                TryExecuteCommand();
                return true;
            }

            if (key == KeyCode.KB_Tab)
            {
                DoAutoComplete();
                return true;
            }

            if (key == KeyCode.KB_LeftShift || key == KeyCode.KB_RightShift)
            {
                shiftPressed = true;
                return true;
            }

            return false;
        }

        private bool OnKeyRepeat(KeyCode key)
        {
            if (key >= KeyCode.KB_A && key <= KeyCode.KB_Z)
            {   
                return OnKeyPressed(key);
            }

            if (key >= KeyCode.KB_D0 && key <= KeyCode.KB_D9 || key >= KeyCode.KB_NumPad0 && key <= KeyCode.KB_NumPad9)
            {
                return OnKeyPressed(key);
            }

            if (key == KeyCode.KB_Left)
            {
                return OnKeyPressed(key);
            }

            if (key == KeyCode.KB_Right)
            {
                return OnKeyPressed(key);
            }

            if (key == KeyCode.KB_Back)
            {
                return OnKeyPressed(key);
            }

            return false;
        }

        private bool OnKeyReleased(KeyCode key)
        {   
            if (key == KeyCode.KB_LeftShift || key == KeyCode.KB_RightShift)
            {
                shiftPressed = false;
                return true;
            }

            return false;
        }
    }
}
