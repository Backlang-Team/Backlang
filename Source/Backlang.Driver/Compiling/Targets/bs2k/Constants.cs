﻿namespace Backlang.Driver.Compiling.Targets.bs2k;

//ToDo: add way to specify aliasing constants -> call by int not by enum
public enum Constants
{
    TERMINAL_CURSOR_MODE_BLINKING = 0,
    TERMINAL_CURSOR_MODE_VISIBLE = 1,
    TERMINAL_CURSOR_MODE_INVISIBLE = 2,

    DISPLAY_HEIGHT = 360,
    DISPLAY_WIDTH = 480,

    TERMINAL_HEIGHT = 25,
    TERMINAL_WIDTH = 80,

    TERMINAL_BUFFER_SIZE = 8000,
    FRAMEBUFFER_SIZE = 691200,

    STACK_SIZE = 524288,
}