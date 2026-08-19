package com.musicmanager.app;

import javax.swing.SwingUtilities;
import javax.swing.UIManager;

import com.musicmanager.data_access.DataAccess;
import com.musicmanager.entity.AppSettings;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCPresenter;
import com.musicmanager.interface_adapter.ConvertLRC.ConvertLRCViewModal;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCInteractor;
import com.musicmanager.view.ConvertLRC.ConvertLRCView;

public class Main {
    public static void main(String[] args) {
        try {
            UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName());
        } catch (Exception e) {
            // fall back to the default look and feel
        }

        SwingUtilities.invokeLater(() -> {
            DataAccess dataAccess = new DataAccess();
            AppSettings settings = dataAccess.load();

            ConvertLRCViewModal viewModal = new ConvertLRCViewModal();
            ConvertLRCPresenter presenter = new ConvertLRCPresenter(viewModal);

            ConvertLRCInteractor interactor = new ConvertLRCInteractor();
            interactor.convertLRC(dataAccess, presenter);

            new ConvertLRCView(dataAccess, interactor, viewModal, dataAccess, settings).setVisible(true);
        });
    }
}
