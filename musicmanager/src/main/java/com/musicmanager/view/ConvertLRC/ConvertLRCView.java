package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Dimension;
import java.io.File;
import java.util.ArrayList;
import java.util.List;
import java.util.Queue;

import javax.swing.BorderFactory;
import javax.swing.JFileChooser;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JOptionPane;
import javax.swing.JPanel;
import javax.swing.JTextField;
import javax.swing.SwingWorker;

import com.musicmanager.entity.AppSettings;
import com.musicmanager.entity.Music;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCViewModal;
import com.musicmanager.use_case.AppSettings.AppSettingsDataAccessInterface;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCDataAccessInterface;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.view.FlatButton;
import com.musicmanager.view.Theme;

/**
 * home screen: pick a music folder, then move on to SongListView. Prefills
 * the path from persisted settings, and remembers it (and the current
 * default formats) whenever a valid folder is confirmed.
 */
public class ConvertLRCView extends JFrame {

    private final ConvertLRCDataAccessInterface dataAccess;
    private final ConvertLRCInputBoundary interactor;
    private final ConvertLRCViewModal viewModal;
    private final AppSettingsDataAccessInterface appSettingsDataAccess;

    private final JTextField pathField = new JTextField();

    public ConvertLRCView(ConvertLRCDataAccessInterface dataAccess, ConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal, AppSettingsDataAccessInterface appSettingsDataAccess,
            AppSettings initialSettings) {
        this.dataAccess = dataAccess;
        this.interactor = interactor;
        this.viewModal = viewModal;
        this.appSettingsDataAccess = appSettingsDataAccess;

        setTitle("Music Manager");
        setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        setSize(Theme.COMMON_WIDTH, Theme.HOME_HEIGHT);
        setLocationRelativeTo(null);

        JPanel content = new JPanel(new BorderLayout(0, Theme.COMPONENT_GAP));
        content.setBackground(Theme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(Theme.WINDOW_PADDING, Theme.WINDOW_PADDING,
                Theme.WINDOW_PADDING, Theme.WINDOW_PADDING));

        JLabel appTitle = new JLabel("Music Manager");
        appTitle.setFont(Theme.titleFont());
        appTitle.setForeground(Theme.TEXT_PRIMARY);
        content.add(appTitle, BorderLayout.NORTH);

        JPanel centerPanel = new JPanel(new BorderLayout(0, Theme.COMPONENT_GAP));
        centerPanel.setOpaque(false);

        JLabel label = new JLabel("Music folder");
        label.setFont(Theme.headerFont());
        label.setForeground(Theme.TEXT_SECONDARY);
        centerPanel.add(label, BorderLayout.NORTH);

        JPanel pathPanel = new JPanel(new BorderLayout(Theme.COMPONENT_GAP, 0));
        pathPanel.setOpaque(false);
        pathField.setFont(Theme.bodyFont());
        pathField.setPreferredSize(new Dimension(280, 40));
        if (initialSettings.getLastPath() != null) {
            pathField.setText(initialSettings.getLastPath());
        }
        pathPanel.add(pathField, BorderLayout.CENTER);

        FlatButton browseButton = FlatButton.secondary("Browse");
        pathPanel.add(browseButton, BorderLayout.EAST);
        centerPanel.add(pathPanel, BorderLayout.CENTER);

        content.add(centerPanel, BorderLayout.CENTER);

        JPanel actionPanel = new JPanel(new BorderLayout());
        actionPanel.setOpaque(false);
        FlatButton convertButton = FlatButton.primary("Convert LRC");
        actionPanel.add(convertButton, BorderLayout.EAST);
        content.add(actionPanel, BorderLayout.SOUTH);

        setContentPane(content);

        browseButton.addActionListener(e -> onBrowse());
        convertButton.addActionListener(e -> onConvert());
    }

    private void onBrowse() {
        File downloads = new File(System.getProperty("user.home"), "Downloads");
        File startDirectory = downloads.isDirectory() ? downloads : new File(System.getProperty("user.home"));

        JFileChooser chooser = new JFileChooser(startDirectory);
        chooser.setDialogTitle("Select Music Folder");
        chooser.setFileSelectionMode(JFileChooser.DIRECTORIES_ONLY);
        chooser.setAcceptAllFileFilterUsed(false);

        int result = chooser.showOpenDialog(this);
        if (result == JFileChooser.APPROVE_OPTION) {
            pathField.setText(chooser.getSelectedFile().getAbsolutePath());
        }
    }

    private void onConvert() {
        String path = pathField.getText().trim();
        File folder = new File(path);

        if (path.isEmpty() || !folder.isDirectory()) {
            JOptionPane.showMessageDialog(this, "Please choose a valid music folder.", "Invalid path",
                    JOptionPane.WARNING_MESSAGE);
            return;
        }

        // reload fresh rather than trusting a field cached at construction time: this
        // window is never disposed when SongListView opens, so a stale in-memory copy
        // would silently revert formats changed via SongListView's Settings dialog
        List<String> currentFormats = ConvertLRCInputData
                .orDefaultFormats(appSettingsDataAccess.load().getDefaultTargetFormats());
        appSettingsDataAccess.save(new AppSettings(path, currentFormats));

        SwingWorker<List<Music>, Void> worker = new SwingWorker<List<Music>, Void>() {
            @Override
            protected List<Music> doInBackground() {
                Queue<Music> musics = dataAccess.getMusics(path);
                return new ArrayList<>(musics);
            }

            @Override
            protected void done() {
                try {
                    new SongListView(path, get(), interactor, viewModal, appSettingsDataAccess, currentFormats)
                            .setVisible(true);
                } catch (Exception e) {
                    JOptionPane.showMessageDialog(ConvertLRCView.this, "Failed to scan the music folder.", "Error",
                            JOptionPane.ERROR_MESSAGE);
                }
            }
        };
        worker.execute();
    }
}
