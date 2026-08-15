package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Dimension;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import javax.swing.BorderFactory;
import javax.swing.DefaultListModel;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JList;
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
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.view.FlatButton;

/**
 * lists the songs found in the chosen path and lets the user pick target
 * formats (via SettingsDialog) and kick off the conversion.
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
        setSize(480, 520);
        setLocationRelativeTo(null);

        JPanel content = new JPanel(new BorderLayout(0, 16));
        content.setBorder(BorderFactory.createEmptyBorder(20, 20, 20, 20));

        JLabel header = new JLabel(musics.size() + " song(s) found");
        header.setHorizontalAlignment(SwingConstants.LEFT);
        content.add(header, BorderLayout.NORTH);

        DefaultListModel<String> listModel = new DefaultListModel<>();
        for (Music music : musics) {
            listModel.addElement(music.getTitle());
        }
        JList<String> songList = new JList<>(listModel);
        songList.setEnabled(false);
        JScrollPane scrollPane = new JScrollPane(songList);
        scrollPane.setPreferredSize(new Dimension(420, 380));
        content.add(scrollPane, BorderLayout.CENTER);

        JPanel buttonPanel = new JPanel(new BorderLayout());
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
                ConvertLRCController controller = new ConvertLRCController(path, selectedFormats, interactor);
                controller.execute();
                return null;
            }
        };
        worker.execute();
    }
}
