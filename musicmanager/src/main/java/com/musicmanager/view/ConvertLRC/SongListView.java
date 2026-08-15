package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.GridLayout;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import javax.swing.BorderFactory;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JOptionPane;
import javax.swing.JPanel;
import javax.swing.JScrollPane;
import javax.swing.SwingConstants;
import javax.swing.SwingUtilities;
import javax.swing.SwingWorker;

import com.musicmanager.entity.Music;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCController;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCState;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCViewModal;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.view.FlatButton;
import com.musicmanager.view.Theme;

/**
 * lists the songs found in the chosen path as a 2-column card grid, and
 * lets the user pick target formats (via SettingsDialog) and kick off the
 * conversion.
 */
public class SongListView extends JFrame {

    private final String path;
    private final ConvertLRCInputBoundary interactor;
    private final ConvertLRCViewModal viewModal;

    private List<String> selectedFormats = new ArrayList<>(Arrays.asList(ConvertLRCInputData.AVAILABLE_FORMATS));

    private final FlatButton startButton = FlatButton.primary("Start");

    public SongListView(String path, List<Music> musics, ConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal) {
        this.path = path;
        this.interactor = interactor;
        this.viewModal = viewModal;

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
            grid.add(new SongCard(music.getTitle()));
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

        settingsButton.addActionListener(e -> new SettingsDialog(this, selectedFormats,
                formats -> this.selectedFormats = formats).setVisible(true));

        startButton.addActionListener(e -> onStart());

        viewModal.addPropertyChangeListener(evt -> {
            if ("state".equals(evt.getPropertyName())) {
                ConvertLRCState state = viewModal.getState();
                SwingUtilities.invokeLater(() -> {
                    JOptionPane.showMessageDialog(this, state.getMessage(), "Conversion result",
                            JOptionPane.INFORMATION_MESSAGE);
                    startButton.setEnabled(true);
                });
            }
        });
    }

    private void onStart() {
        if (selectedFormats.isEmpty()) {
            JOptionPane.showMessageDialog(this, "Select at least one target format in Settings.",
                    "No formats selected", JOptionPane.WARNING_MESSAGE);
            return;
        }

        startButton.setEnabled(false);

        SwingWorker<Void, Void> worker = new SwingWorker<Void, Void>() {
            @Override
            protected Void doInBackground() {
                ConvertLRCController controller = new ConvertLRCController(interactor);
                controller.execute(path, selectedFormats);
                return null;
            }
        };
        worker.execute();
    }
}
