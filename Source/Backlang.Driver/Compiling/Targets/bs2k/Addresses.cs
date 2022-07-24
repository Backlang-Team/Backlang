﻿namespace Backlang.Driver.Compiling.Targets.bs2k;

//ToDo: Figure out address values for enum and add rest: https://github.com/Backseating-Committee-2k/Upholsterer2k/blob/main/constants.c
public enum Addresses
{
    ENTRY_POINT = 1914696,

    STACK_START = 1390408,

    FIRST_FRAMEBUFFER_START = 8008,
    SECOND_FRAMEBUFFER_START = 699208,

    TERMINAL_CURSOR_MODE = 8004,

    TERMINAL_CURSOR_POINTER = 8000,

    TERMINAL_BUFFER_START = 0,
    TERMINAL_BUFFER_END = 8000,
}