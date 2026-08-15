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
import javax.swing.JPanel;
import javax.swing.SwingConstants;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;
import com.musicmanager.view.FlatButton;

/**
 * modal dialog letting the user pick which of the available target formats
 * to convert lyrics into.
 */
public class SettingsDialog extends JDialog {

    private final Map<String, JCheckBox> formatCheckBoxes = new LinkedHashMap<>();

    public SettingsDialog(Frame owner, List<String> selectedFormats, Consumer<List<String>> onSave) {
        super(owner, "Settings", true);

        JPanel content = new JPanel(new BorderLayout(0, 16));
        content.setBorder(BorderFactory.createEmptyBorder(20, 24, 20, 24));

        JLabel title = new JLabel("Target formats");
        title.setHorizontalAlignment(SwingConstants.LEFT);
        content.add(title, BorderLayout.NORTH);

        JPanel checkBoxPanel = new JPanel(new GridLayout(ConvertLRCInputData.AVAILABLE_FORMATS.length, 1, 0, 8));
        for (String format : ConvertLRCInputData.AVAILABLE_FORMATS) {
            JCheckBox checkBox = new JCheckBox(format, selectedFormats.contains(format));
            formatCheckBoxes.put(format, checkBox);
            checkBoxPanel.add(checkBox);
        }
        content.add(checkBoxPanel, BorderLayout.CENTER);

        JPanel buttonPanel = new JPanel();
        FlatButton saveButton = FlatButton.primary("Save");
        FlatButton cancelButton = FlatButton.secondary("Cancel");
        buttonPanel.add(cancelButton);
        buttonPanel.add(saveButton);
        content.add(buttonPanel, BorderLayout.SOUTH);

        saveButton.addActionListener(e -> {
            List<String> selected = new ArrayList<>();
            for (Map.Entry<String, JCheckBox> entry : formatCheckBoxes.entrySet()) {
                if (entry.getValue().isSelected()) {
                    selected.add(entry.getKey());
                }
            }
            onSave.accept(selected);
            dispose();
        });
        cancelButton.addActionListener(e -> dispose());

        setContentPane(content);
        setResizable(false);
        pack();
        setLocationRelativeTo((Component) owner);
    }
}
