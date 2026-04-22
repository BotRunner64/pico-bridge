#!/usr/bin/env python3
"""Compatibility wrapper for local development."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))

from pico_bridge.cli import build_parser, main

__all__ = ["build_parser", "main"]


if __name__ == "__main__":
    main()
