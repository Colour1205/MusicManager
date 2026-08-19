package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Component;
import java.awt.Frame;
import java.awt.GridLayout;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

import javax.swing.BorderFactory;
import javax.swing.JCheckBox;
import javax.swing.JDialog;
import javax.swing.JLabel;
import javax.swing.JOptionPane;
import javax.swing.JPanel;
import javax.swing.SwingConstants;

import com.musicmanager.entity.Music;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.view.FlatButton;
import com.musicmanager.view.Theme;

/**
 * modal dialog for converting a single song: lets the user pick target
 * formats just for this song without touching the app's general Settings,
 * then triggers the conversion.
 */
public class SongConvertDialog extends JDialog {

    private final Map<String, JCheckBox> formatCheckBoxes = new LinkedHashMap<>();

    public SongConvertDialog(Frame owner, Music music, List<String> defaultFormats,
            Consumer<List<String>> onConvert) {
        super(owner, "Convert " + music.getTitle(), true);

        JPanel content = new JPanel(new BorderLayout(0, Theme.COMPONENT_GAP));
        content.setBackground(Theme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(Theme.WINDOW_PADDING, Theme.WINDOW_PADDING,
                Theme.WINDOW_PADDING, Theme.WINDOW_PADDING));

        JLabel title = new JLabel(music.getTitle());
        title.setFont(Theme.headerFont());
        title.setForeground(Theme.TEXT_PRIMARY);
        title.setHorizontalAlignment(SwingConstants.LEFT);
        content.add(title, BorderLayout.NORTH);

        JPanel checkBoxPanel = new JPanel(
                new GridLayout(ConvertLRCInputData.AVAILABLE_FORMATS.length, 1, 0, Theme.COMPONENT_GAP));
        checkBoxPanel.setBackground(Theme.SURFACE);
        checkBoxPanel.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(Theme.BORDER, 1),
                BorderFactory.createEmptyBorder(Theme.COMPONENT_GAP, Theme.COMPONENT_GAP, Theme.COMPONENT_GAP,
                        Theme.COMPONENT_GAP)));
        for (String format : ConvertLRCInputData.AVAILABLE_FORMATS) {
            JCheckBox checkBox = new JCheckBox(format, defaultFormats.contains(format));
            checkBox.setFont(Theme.bodyFont());
            checkBox.setForeground(Theme.TEXT_PRIMARY);
            checkBox.setOpaque(false);
            formatCheckBoxes.put(format, checkBox);
            checkBoxPanel.add(checkBox);
        }
        content.add(checkBoxPanel, BorderLayout.CENTER);

        JPanel buttonPanel = new JPanel();
        buttonPanel.setOpaque(false);
        FlatButton convertButton = FlatButton.primary("Convert");
        FlatButton cancelButton = FlatButton.secondary("Cancel");
        buttonPanel.add(cancelButton);
        buttonPanel.add(convertButton);
        content.add(buttonPanel, BorderLayout.SOUTH);

        convertButton.addActionListener(e -> {
            List<String> selected = new ArrayList<>();
            for (Map.Entry<String, JCheckBox> entry : formatCheckBoxes.entrySet()) {
                if (entry.getValue().isSelected()) {
                    selected.add(entry.getKey());
                }
            }

            if (selected.isEmpty()) {
                JOptionPane.showMessageDialog(this, "Select at least one target format.", "No formats selected",
                        JOptionPane.WARNING_MESSAGE);
                return;
            }

            onConvert.accept(selected);
            dispose();
        });
        cancelButton.addActionListener(e -> dispose());

        setContentPane(content);
        setResizable(false);
        setSize(Theme.SETTINGS_WIDTH, Theme.SETTINGS_HEIGHT);
        setLocationRelativeTo((Component) owner);
    }
}
