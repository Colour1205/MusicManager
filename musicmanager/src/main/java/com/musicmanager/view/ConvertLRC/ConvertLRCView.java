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

import com.musicmanager.entity.Music;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCViewModal;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCDataAccessInterface;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;
import com.musicmanager.view.FlatButton;

/**
 * home screen: pick a music folder, then move on to SongListView.
 */
public class ConvertLRCView extends JFrame {

    private final ConvertLRCDataAccessInterface dataAccess;
    private final ConvertLRCInputBoundary interactor;
    private final ConvertLRCViewModal viewModal;

    private final JTextField pathField = new JTextField();

    public ConvertLRCView(ConvertLRCDataAccessInterface dataAccess, ConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal) {
        this.dataAccess = dataAccess;
        this.interactor = interactor;
        this.viewModal = viewModal;

        setTitle("Music Manager");
        setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        setSize(480, 220);
        setLocationRelativeTo(null);

        JPanel content = new JPanel(new BorderLayout(0, 20));
        content.setBorder(BorderFactory.createEmptyBorder(24, 24, 24, 24));

        JLabel label = new JLabel("Music folder");
        content.add(label, BorderLayout.NORTH);

        JPanel pathPanel = new JPanel(new BorderLayout(8, 0));
        pathField.setPreferredSize(new Dimension(280, 32));
        pathPanel.add(pathField, BorderLayout.CENTER);

        FlatButton browseButton = FlatButton.secondary("Browse");
        pathPanel.add(browseButton, BorderLayout.EAST);
        content.add(pathPanel, BorderLayout.CENTER);

        JPanel actionPanel = new JPanel(new BorderLayout());
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

        Queue<Music> musics = dataAccess.getMusics(path);
        new SongListView(path, new ArrayList<>(musics), interactor, viewModal).setVisible(true);
    }
}
