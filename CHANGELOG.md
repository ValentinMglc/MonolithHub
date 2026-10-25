# Changelog

Toutes les évolutions notables de Monolith Hub. Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),
numérotation [SemVer](https://semver.org/lang/fr/).

## [1.1.0] - 2026-10-25

### Ajouté
- L'installateur propose d'installer le pilote PawnIO (case cochée par défaut, affichée seulement s'il manque),
  pour avoir dès le premier lancement la température, la fréquence et la puissance du processeur
- Modèles de tickets GitHub : signaler un bug, demander la prise en charge d'un modèle

## [1.0.0] - 2026-10-25

Première version publique : une alternative légère et sans publicité à l'OMEN Gaming Hub.

### Monitoring
- Tableau de bord : utilisation, température, fréquence et puissance du CPU et du GPU, mémoire, VRAM
- Graphiques des 60 dernières secondes, réseau (réception / envoi), espace disque, processus les plus actifs
- Page Capteurs avec tous les capteurs, valeurs min / max et recherche
- Page Système : modèle, BIOS, processeur, carte graphique, mémoire, disques, audio
- Détection du pilote PawnIO (température CPU) avec installation en un clic

### Éclairage (HP OMEN Obelisk 875, contrôleur USB `103C:84FD`)
- Logo et barre lumineuse : statique, respiration, clignotement, cycle de couleurs, éteint
- Aperçu animé du boîtier, palette, teinte personnalisée, luminosité, vitesse
- Profils (Jeu, Nuit, Arc-en-ciel, Éteint et les vôtres) avec raccourcis Ctrl+Alt+1…9, Ctrl+Alt+0 pour allumer / éteindre
- Couleur selon la température du CPU ou du GPU
- Reconnexion automatique du contrôleur et reprise après la mise en veille

### Application
- Icône dans la zone de notification, démarrage avec Windows, alertes de chauffe
- Interface en français et en anglais, changement de langue à chaud
- Installateur (runtime .NET inclus) et version portable
- Journal d'erreurs et nettoyage complet à la désinstallation

### Limites connues
- Monolith Hub n'est pas encore signé numériquement : Windows peut afficher un avertissement SmartScreen,
  et le Contrôle intelligent des applications de Windows 11 bloque son installation lorsqu'il est activé.
- L'éclairage n'est pris en charge que sur le contrôleur `103C:84FD` (OMEN Obelisk 875, probablement OMEN 30L).
