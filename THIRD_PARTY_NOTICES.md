# Composants tiers

| Composant | Utilisation | Licence |
|---|---|---|
| [Chainsaw](https://github.com/WithSecureOpenSource/chainsaw) (WithSecure) | Moteur de détection, lié via `native-src/chainsaw_ffi` | GPL-3.0 |
| [Sigma](https://github.com/SigmaHQ/sigma) (règles) | Règles de détection embarquées dans `Resources/rules.zip` | Detection Rule License (DRL) 1.1 |
| [Gemma 3 1B](https://huggingface.co/google/gemma-3-1b-it) (Google), quantifié en GGUF | Résumé IA local, téléchargé par `scripts/build.ps1` | [Conditions d'utilisation Gemma](https://ai.google.dev/gemma/terms) |
| [llama.cpp](https://github.com/ggml-org/llama.cpp) | Inférence du modèle (via `native/lamazero.dll`) | MIT |
| [Mermaid](https://github.com/mermaid-js/mermaid) | Diagrammes du résumé (`Assets/mermaid.min.js`) | MIT |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), [CommunityToolkit](https://github.com/CommunityToolkit/Windows) | Interface WinUI 3 | MIT |

Les journaux d'exemple utilisés pour les captures d'écran proviennent de
[EVTX-ATTACK-SAMPLES](https://github.com/sbousseaden/EVTX-ATTACK-SAMPLES) ; ils ne sont pas redistribués dans ce dépôt.
