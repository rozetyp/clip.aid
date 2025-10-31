# ClipAid - Private AI Text Assistant

**Your text, your computer, your AI - Zero cloud, zero tracking, zero compromise.**

---

## 🎯 What Is ClipAid?

ClipAid is a **privacy-first alternative to Grammarly** that improves your text using YOUR local AI instead of sending your data to someone else's cloud.

**The Problem with Grammarly:**
- ❌ Sends your text to their servers
- ❌ You don't know what they do with it
- ❌ Requires internet connection
- ❌ Closed-source (can't verify behavior)
- ❌ Subscription required

**ClipAid's Solution:**
- ✅ Text stays on YOUR computer
- ✅ Uses YOUR local AI (LM Studio, Ollama, etc.)
- ✅ Works offline
- ✅ Open source (898 lines you can read)
- ✅ Free forever

---

## 🚀 Quick Start

### **1. Install Local AI (Choose One)**

**Option A: LM Studio** (Recommended for beginners)
1. Download from https://lmstudio.ai
2. Install and open LM Studio
3. Download any model (e.g., "Phi-3-mini")
4. Click "Start Server" (default port: 1234)

**Option B: Ollama**
1. Download from https://ollama.ai
2. Install and run: `ollama serve`
3. Pull a model: `ollama pull llama3`

**Option C: Any OpenAI-compatible API**
- LocalAI, text-generation-webui, or any other
- Must support `/v1/chat/completions` endpoint

### **2. Run ClipAid**

1. Download `ClipAid-Optimized.exe`
2. Double-click to run
3. Icon appears in system tray (bottom-right, near clock)
4. Copy any text (Ctrl+C)
5. Popup appears with improved version!

**That's it!** No installation, no registration, no cloud signup.

---

## 🔒 Privacy Guarantee

### **Where Your Text Goes (Complete Disclosure)**

```
You copy text → ClipAid reads clipboard → Sends to http://127.0.0.1:1234 → Your local AI processes it → Result shows in popup
```

**127.0.0.1 = Your Computer** (localhost, never leaves your machine)

### **What We DON'T Do**

❌ Never send data to external servers  
❌ Never collect analytics or telemetry  
❌ Never log your text  
❌ Never phone home  
❌ Never check for updates online  
❌ Never require registration  
❌ Never show ads  
❌ Never track you  

### **Proof**

1. **Open Source:** Read all 994 lines in `ClipAid-Pro.cs`
2. **Single File:** No hidden DLLs or dependencies
3. **Network Monitor:** Use Wireshark - you'll see ONLY localhost traffic
4. **File Monitor:** Use Process Monitor - you'll see ONLY `ClipAid.config.json`

See [TRANSPARENCY_REPORT.md](TRANSPARENCY_REPORT.md) for complete code documentation.

---

## 📖 How To Use

### **Basic Usage**

1. Copy any text (Ctrl+C)
2. Wait 500ms (filters accidental copies)
3. Popup appears with improved text
4. Click "Use" to copy improved version
5. Or click "Use & Paste" to paste automatically

### **Actions**

ClipAid offers 5 quick actions:

| Action | What It Does | Temperature |
|--------|--------------|-------------|
| **Improve** | Fix grammar, spelling, punctuation | 0.3 (precise) |
| **Formal** | Make professional and formal | 0.1 (very precise) |
| **Casual** | Rewrite in casual tone | 0.5 (moderate) |
| **Short** | Condense and shorten | 0.3 (precise) |
| **Reword** | Say same thing differently | 0.7 (creative) |

**Change Action:** Click "Try Different" button to see all options

**Custom Prompt:** Click "✎ Custom" to enter your own instruction

### **Keyboard Shortcuts**

- **Esc** - Close popup
- **Ctrl+Enter** - Use & Paste

### **Settings**

Right-click system tray icon → Settings

**What You Can Configure:**
- API Endpoint (if not using default port 1234)
- Max tokens (response length limit)
- Default action (which runs automatically)
- All 5 action prompts (customize instructions)
- Temperatures (AI creativity levels)
- System prompt (master AI behavior instruction)

---

## ⚙️ Configuration

### **Default Settings**

```json
{
  "Endpoint": "http://127.0.0.1:1234/v1/chat/completions",
  "MaxTokens": 256,
  "SystemPrompt": "Edit the text and return only the improved version. No explanations.",
  "DefaultAction": "Improve"
}
```

Stored in: `ClipAid.config.json` (same folder as .exe)

### **Change AI Server Port**

If your AI runs on different port (e.g., 11434 for Ollama):

1. Right-click tray icon → Settings
2. Change endpoint to: `http://127.0.0.1:11434/v1/chat/completions`
3. Click OK

### **Customize Prompts**

Want "Improve" to also fix capitalization?

1. Right-click tray icon → Settings
2. Find "Improve Prompt" textbox
3. Edit to: `"Fix all grammar, spelling, punctuation, and capitalization errors..."`
4. Click OK

---

## 🛠️ Troubleshooting

### **Popup says "⚠️ Cannot connect to AI server"**

**Fix:**
1. Check your local AI is running (LM Studio, Ollama, etc.)
2. Verify server is on port 1234 (or change ClipAid settings)
3. Try opening http://127.0.0.1:1234 in browser - should see API docs

### **Nothing happens when I copy text**

**Check:**
1. ClipAid icon in system tray? (may be hidden - click ^ arrow)
2. Copied at least 5 characters? (minimum requirement)
3. Copied less than 5000 characters? (maximum limit)
4. Waited 500ms after copying? (accidental copy filter)

### **Popup appears in wrong position**

**This is normal** - ClipAid positions popup near cursor but:
- Stays within screen bounds (20px margin)
- Won't overlap taskbar or screen edges
- If multi-monitor, appears on same screen as cursor

### **Want to disable for certain apps?**

Currently not supported, but workaround:
- Right-click tray icon → Exit (when working with sensitive data)
- Re-run ClipAid when done

---

## 🔧 Advanced Usage

### **Build From Source**

Verify the code yourself:

```powershell
# Compile (requires .NET Framework 4.0 - built into Windows)
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:ClipAid-Optimized.exe ClipAid-Pro.cs

# Your build should match provided .exe
```

### **Use With Different AI Models**

ClipAid works with ANY OpenAI-compatible API:
- LM Studio (local)
- Ollama (local)
- text-generation-webui (local)
- LocalAI (local)
- vLLM (local or remote)
- Even real OpenAI API (if you trust them with your data)

Just change endpoint in settings!

### **Multiple Configurations**

Want different settings for work vs personal?

1. Copy `ClipAid.config.json` to `ClipAid-Work.config.json`
2. Edit work config
3. Run ClipAid with: `ClipAid-Optimized.exe` (uses ClipAid.config.json)
4. Or: Rename configs when switching contexts

---

## 📊 System Requirements

- **OS:** Windows 7 or later (Vista might work)
- **Framework:** .NET Framework 4.0 (pre-installed on Windows 7+)
- **RAM:** ~20 MB (tiny!)
- **CPU:** Any (runs on background thread)
- **Disk:** < 1 MB
- **Network:** None (unless you count localhost)
- **Local AI:** LM Studio, Ollama, or compatible server

---

## 🤝 Contributing

### **Report Issues**

If you find ANY behavior not documented:
1. Check `CLIPAID_CODE_ANALYSIS.md` (line numbers provided)
2. Search ClipAid-Pro.cs for the function
3. Report with: What happened vs what documentation says

### **Suggest Features**

Feature requests welcome! Especially:
- UI improvements
- New actions
- Better prompts
- Performance optimizations

### **Code Contributions**

Pull requests must maintain transparency:
- Keep single-file architecture
- Add inline comments for complex logic
- Update CLIPAID_CODE_ANALYSIS.md
- No external dependencies
- No network calls except localhost AI

---

## 📜 License

**MIT License** - Do whatever you want with it!

```
Copyright (c) 2025 ClipAid

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies, with no restrictions.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
```

---

## 🙏 Credits

**Why This Exists:**

People are tired of not knowing what Grammarly does with their data. Whether it's a confidential work email, a private journal entry, or a sensitive document - your text shouldn't leave your computer without your explicit consent.

**Built for:**
- Privacy advocates
- Security-conscious professionals
- Writers who value data ownership
- Anyone who wants transparent tools

**Inspired by:**
- The open-source community
- Local AI movement (LM Studio, Ollama)
- Right to privacy

---

## 📞 Contact

- **Issues:** Check CLIPAID_CODE_ANALYSIS.md first
- **Questions:** Read ClipAid-Pro.cs (994 lines, very readable)
- **Verification:** Build from source and compare

**Remember:** If you can't read the code, you can't trust the app. That's why we keep it simple, transparent, and fully documented.

---

## 🎉 Thank You!

For choosing transparency over convenience.  
For valuing privacy over features.  
For demanding better from your tools.

**Your text, your rules.** 🔒
