package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.GridLayout;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import javax.swing.BorderFactory;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JOptionPane;
import javax.swing.JPanel;
import javax.swing.JScrollPane;
import javax.swing.SwingConstants;
import javax.swing.SwingUtilities;
import javax.swing.SwingWorker;

import com.musicmanager.entity.AppSettings;
import com.musicmanager.entity.Music;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCController;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCState;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCViewModal;
import com.musicmanager.use_case.AppSettings.AppSettingsDataAccessInterface;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.view.FlatButton;
import com.musicmanager.view.Theme;

/**
 * lists the songs found in the chosen path as a 2-column card grid. Each
 * song can be converted individually (click its card) or all at once (the
 * Start button); the Settings button picks the general target formats,
 * which are persisted for next launch alongside the current path.
 */
public class SongListView extends JFrame {

    private final String path;
    private final List<Music> musics;
    private final ConvertLRCInputBoundary interactor;
    private final ConvertLRCViewModal viewModal;
    private final AppSettingsDataAccessInterface appSettingsDataAccess;

    private final Map<String, SongCard> cardsByMusicPath = new LinkedHashMap<>();

    private List<String> selectedFormats;
    private boolean expectingBatchSummary;

    // only one conversion (batch or single-song) may run at a time: overlapping
    // conversions would race on the same output files and on the shared
    // ConvertLRCState/ViewModel
    private boolean busy;

    private final FlatButton startButton = FlatButton.primary("Start");

    public SongListView(String path, List<Music> musics, ConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal, AppSettingsDataAccessInterface appSettingsDataAccess,
            List<String> initialFormats) {
        this.path = path;
        this.musics = musics;
        this.interactor = interactor;
        this.viewModal = viewModal;
        this.appSettingsDataAccess = appSettingsDataAccess;
        this.selectedFormats = new ArrayList<>(ConvertLRCInputData.orDefaultFormats(initialFormats));

        setTitle("Songs in " + path);
        setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
        setSize(Theme.COMMON_WIDTH, Theme.SONG_LIST_HEIGHT);
        setLocationRelativeTo(null);

        JPanel content = new JPanel(new BorderLayout(0, Theme.COMPONENT_GAP));
        content.setBackground(Theme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(Theme.WINDOW_PADDING, Theme.WINDOW_PADDING,
                Theme.WINDOW_PADDING, Theme.WINDOW_PADDING));

        JLabel header = new JLabel(musics.size() + " song(s) found");
        header.setFont(Theme.headerFont());
        header.setForeground(Theme.TEXT_SECONDARY);
        header.setHorizontalAlignment(SwingConstants.LEFT);
        content.add(header, BorderLayout.NORTH);

        JPanel grid = new JPanel(new GridLayout(0, 2, Theme.CARD_GAP, Theme.CARD_GAP));
        grid.setOpaque(false);
        for (Music music : musics) {
            SongCard card = new SongCard(music, this::onSongClicked);
            cardsByMusicPath.put(music.getPath(), card);
            grid.add(card);
        }

        // GridLayout stretches rows to fill a JScrollPane's viewport height, which
        // bloats cards when there are few songs. Docking it NORTH in a holder panel
        // keeps rows at their natural, fixed height instead.
        JPanel gridHolder = new JPanel(new BorderLayout());
        gridHolder.setOpaque(false);
        gridHolder.add(grid, BorderLayout.NORTH);

        JScrollPane scrollPane = new JScrollPane(gridHolder);
        scrollPane.setBorder(BorderFactory.createEmptyBorder());
        scrollPane.setHorizontalScrollBarPolicy(JScrollPane.HORIZONTAL_SCROLLBAR_NEVER);
        scrollPane.getVerticalScrollBar().setUnitIncrement(16);
        scrollPane.getViewport().setBackground(Theme.BACKGROUND);
        content.add(scrollPane, BorderLayout.CENTER);

        JPanel buttonPanel = new JPanel(new BorderLayout());
        buttonPanel.setOpaque(false);
        FlatButton settingsButton = FlatButton.secondary("Settings");
        buttonPanel.add(settingsButton, BorderLayout.WEST);
        buttonPanel.add(startButton, BorderLayout.EAST);
        content.add(buttonPanel, BorderLayout.SOUTH);

        setContentPane(content);

        settingsButton.addActionListener(e -> new SettingsDialog(this, selectedFormats, formats -> {
            this.selectedFormats = formats;
            appSettingsDataAccess.save(new AppSettings(path, formats));
        }).setVisible(true));

        startButton.addActionListener(e -> onStart());

        // ConvertLRCState is a single long-lived mutable object shared by every
        // conversion; the interactor can mutate and re-fire it again (e.g. the next
        // song in a batch) before the EDT gets around to running a queued Runnable,
        // so the fields this listener cares about must be captured into local
        // variables synchronously, right here, rather than read lazily inside
        // invokeLater
        viewModal.addPropertyChangeListener(evt -> {
            if ("progress".equals(evt.getPropertyName())) {
                ConvertLRCState state = viewModal.getState();
                String musicPath = state.getLastProgressMusicPath();
                boolean success = state.isLastProgressSuccess();
                SwingUtilities.invokeLater(() -> {
                    SongCard card = cardsByMusicPath.get(musicPath);
                    if (card != null) {
                        card.showResult(success);
                    }
                });
            } else if ("state".equals(evt.getPropertyName())) {
                ConvertLRCState state = viewModal.getState();
                String message = state.getMessage();
                boolean showSummary = expectingBatchSummary;
                SwingUtilities.invokeLater(() -> {
                    busy = false;
                    startButton.setEnabled(true);
                    if (showSummary) {
                        JOptionPane.showMessageDialog(this, message, "Conversion result",
                                JOptionPane.INFORMATION_MESSAGE);
                    }
                });
            }
        });
    }

    /** clicking a song shows conversion options scoped to just that song */
    private void onSongClicked(Music music) {
        if (busy) {
            JOptionPane.showMessageDialog(this, "A conversion is already in progress. Please wait for it to finish.",
                    "Conversion in progress", JOptionPane.INFORMATION_MESSAGE);
            return;
        }

        new SongConvertDialog(this, music, selectedFormats,
                formats -> convertSongs(Collections.singletonList(music), formats, false)).setVisible(true);
    }

    private void onStart() {
        if (busy) {
            return;
        }

        if (selectedFormats.isEmpty()) {
            JOptionPane.showMessageDialog(this, "Select at least one target format in Settings.",
                    "No formats selected", JOptionPane.WARNING_MESSAGE);
            return;
        }

        convertSongs(musics, selectedFormats, true);
    }

    private void convertSongs(List<Music> songsToConvert, List<String> formats, boolean isBatch) {
        busy = true;
        expectingBatchSummary = isBatch;
        startButton.setEnabled(false);

        for (Music music : songsToConvert) {
            SongCard card = cardsByMusicPath.get(music.getPath());
            if (card != null) {
                card.showConverting();
            }
        }

        SwingWorker<Void, Void> worker = new SwingWorker<Void, Void>() {
            @Override
            protected Void doInBackground() {
                ConvertLRCController controller = new ConvertLRCController(interactor);
                controller.execute(songsToConvert, formats);
                return null;
            }
        };
        worker.execute();
    }
}
